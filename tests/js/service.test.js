/*
 * Runs the TV background service (tvapp/service/service.js) in Node with a fake "webos-service"
 * module and a fake SSAP endpoint on 127.0.0.1:3000 (the TV's own API), and checks the
 * overlay open/close logic. Optional args <port> <pcIdHex> point discovery at a real PC agent.
 * Usage: node tests/js/service.test.js [port pcIdHex]
 */
"use strict";

const assert = require("assert");
const crypto = require("crypto");
const dgram = require("dgram");
const fs = require("fs");
const http = require("http");
const Module = require("module");
const os = require("os");
const path = require("path");
const EventEmitter = require("events");

const APP_ID = "com.couchpotato.tv";
const [agentPort, agentPcId] = process.argv.slice(2);

// ---------- fake webos-service ----------

let service = null;
function FakeService() {
	this.methods = {};
	this.calls = [];
	this.activityManager = { create() {} };
	service = this;
}
FakeService.prototype.register = function (name, onRequest, onCancel) {
	this.methods[name] = { onRequest, onCancel };
	return new EventEmitter();
};
FakeService.prototype.call = function (uri, args, callback) {
	this.calls.push({ uri, args });
	if (callback) setImmediate(() => callback({ payload: { returnValue: true } }));
};
FakeService.prototype.subscribe = function () {
	return new EventEmitter();
};

let token = 0;
function invoke(name, payload, onEachResponse) {
	return new Promise((resolve) => {
		const message = {
			payload,
			isSubscription: !!payload.subscribe,
			uniqueToken: "t" + ++token,
			respond(r) {
				if (onEachResponse) onEachResponse(r);
				resolve(r);
			}
		};
		service.methods[name].onRequest(message);
	});
}

// ---------- fake SSAP endpoint ----------

const ssapReceived = [];
let ssapSocket = null;

function sendText(socket, object) {
	const payload = Buffer.from(JSON.stringify(object));
	const header = payload.length < 126 ? Buffer.from([0x81, payload.length]) : Buffer.from([0x81, 126, payload.length >> 8, payload.length & 255]);
	socket.write(Buffer.concat([header, payload]));
}

function foreground(appId) {
	sendText(ssapSocket, { type: "response", id: "foreground", payload: { appId, returnValue: true } });
}

const ssapServer = http.createServer().on("upgrade", (req, socket) => {
	const accept = crypto.createHash("sha1").update(req.headers["sec-websocket-key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
	socket.write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
	ssapSocket = socket;
	let buffered = Buffer.alloc(0);
	socket.on("data", (chunk) => {
		buffered = Buffer.concat([buffered, chunk]);
		while (buffered.length >= 6) {
			assert.ok(buffered[1] & 0x80, "client frames must be masked");
			let length = buffered[1] & 0x7f, offset = 2;
			if (length === 126) { length = buffered.readUInt16BE(2); offset = 4; }
			if (buffered.length < offset + 4 + length) return;
			const mask = buffered.slice(offset, offset + 4);
			const data = Buffer.from(buffered.slice(offset + 4, offset + 4 + length).map((b, i) => b ^ mask[i & 3]));
			buffered = buffered.slice(offset + 4 + length);
			const m = JSON.parse(data.toString());
			ssapReceived.push(m);
			if (m.type === "hello") sendText(socket, { type: "hello", payload: {} });
			if (m.type === "register") sendText(socket, { type: "registered", id: m.id, payload: { "client-key": "test-client-key" } });
		}
	});
});

// ---------- run ----------

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
async function until(condition, what) {
	for (let i = 0; i < 100 && !condition(); i++) await wait(50);
	assert.ok(condition(), "timed out waiting for " + what);
}
const launches = () => service.calls.filter((c) => c.uri === "luna://com.webos.applicationManager/launch" && c.args.id === APP_ID).length;

(async () => {
	await new Promise((resolve, reject) => ssapServer.once("error", reject).listen(3000, "127.0.0.1", resolve));

	// Load the service from a copy, so its settings.json doesn't land in the repository.
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), "mr-service-"));
	fs.copyFileSync(path.join(__dirname, "../../tvapp/service/service.js"), path.join(dir, "service.js"));
	const load = Module._load;
	Module._load = function (request) {
		return request === "webos-service" ? FakeService : load.apply(this, arguments);
	};
	require(path.join(dir, "service.js"));

	const pcId = agentPcId || "ffeeddccbbaa99887766554433221100";
	assert.strictEqual((await invoke("configure", { pcId, port: 41234, mac: "aa:bb:cc:dd:ee:ff", broadcast: "127.0.0.1", inputAppId: "com.webos.app.hdmi2" })).returnValue, true);

	// SSAP: hello → register (prompt, no key yet) → subscribe to the foreground app.
	await until(() => ssapReceived.some((m) => m.type === "subscribe"), "SSAP subscribe");
	const register = ssapReceived.find((m) => m.type === "register");
	assert.strictEqual(register.payload.pairingType, "PROMPT");
	assert.deepStrictEqual(register.payload.manifest.permissions, ["READ_RUNNING_APPS"]);
	assert.strictEqual(ssapReceived.find((m) => m.type === "subscribe").uri, "ssap://com.webos.applicationManager/getForegroundAppInfo");
	await until(() => JSON.parse(fs.readFileSync(path.join(dir, "settings.json"), "utf8")).ssapKey === "test-client-key", "client key saved");
	assert.strictEqual((await invoke("status", {})).watcher, "ok");

	// Switching to the PC's input opens the overlay; leaving it tells the overlay to close.
	const closes = [];
	await invoke("close", { subscribe: true }, (r) => closes.push(r));
	foreground("com.webos.app.hdmi2");
	await until(() => launches() === 1, "overlay launch");
	assert.deepStrictEqual(service.calls.find((c) => c.args.id === APP_ID).args.params, { overlay: true });
	foreground(APP_ID); // our own window must not count as leaving
	foreground("netflix");
	await until(() => closes.some((r) => r.close), "close notification");

	// After the user dismisses the overlay it stays closed until they leave the input and come back.
	await invoke("dismiss", {});
	foreground("com.webos.app.hdmi2");
	await wait(200);
	assert.strictEqual(launches(), 1, "relaunched after dismiss");
	foreground("netflix");
	foreground("com.webos.app.hdmi2");
	await until(() => launches() === 2, "relaunch after coming back");

	// Wake-on-LAN must not crash, even with an unreachable target, and must reject a bad MAC.
	assert.strictEqual((await invoke("wake", { mac: "aa:bb:cc:dd:ee:ff", broadcast: "127.0.0.1" })).returnValue, true);
	assert.strictEqual((await invoke("wake", { mac: "nonsense", broadcast: "127.0.0.1" })).returnValue, false);

	// Discovery: against the real PC agent when given, otherwise a stand-in responder.
	let port = Number(agentPort);
	let responder = null;
	if (!port) {
		responder = dgram.createSocket("udp4");
		responder.on("message", (msg, from) => {
			if (msg.toString("ascii", 0, 11) !== "MRDISCOVER1" || msg.toString("hex", 11) !== pcId) return;
			const reply = Buffer.concat([Buffer.from("MRHERE1"), Buffer.from(pcId, "hex"), Buffer.from([0x32, 0xa3])]);
			responder.send(reply, from.port, from.address);
		});
		await new Promise((r) => responder.bind(0, "127.0.0.1", r));
		port = responder.address().port;
	}
	const found = await invoke("discover", { pcId, port, broadcast: "127.0.0.1" });
	assert.strictEqual(found.returnValue, true, "discovery failed: " + JSON.stringify(found));
	assert.strictEqual(found.host, "127.0.0.1");
	assert.strictEqual(found.port, agentPort ? port : 41778);
	const missing = await invoke("discover", { pcId: "00".repeat(16), port, broadcast: "127.0.0.1" });
	assert.strictEqual(missing.returnValue, false);

	if (responder) responder.close();
	console.log("ok");
	process.exit(0);
})().catch((e) => {
	console.error(e.stack || e);
	process.exit(1);
});
