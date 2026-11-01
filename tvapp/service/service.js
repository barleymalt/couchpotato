/*
 * Couchpotato background service (webOS JS service, ES5 for old Node versions).
 *  - watches which app is in the foreground and opens the overlay on the PC's HDMI input
 *  - sends Wake-on-LAN packets and finds the PC by UDP broadcast
 * It never sees the pairing key: that stays in the app.
 */
"use strict";

var Service = require("webos-service");
var dgram = require("dgram");
var fs = require("fs");
var path = require("path");
var http = require("http");
var https = require("https");
var crypto = require("crypto");

var APP_ID = "com.couchpotato.tv";
var SERVICE_ID = APP_ID + ".service";
var SETTINGS_FILE = path.join(__dirname, "settings.json");

var service = new Service(SERVICE_ID);
var settings = loadSettings(); // { pcId, port, mac, broadcast, inputAppId, ssapKey }

/* ---------- helpers ---------- */

function newBuffer(size) {
	if (Buffer.alloc) return Buffer.alloc(size);
	var b = new Buffer(size);
	b.fill(0);
	return b;
}

function bufferFrom(value, encoding) {
	return Buffer.from && Buffer.from !== Uint8Array.from ? Buffer.from(value, encoding) : new Buffer(value, encoding);
}

function loadSettings() {
	try {
		return JSON.parse(fs.readFileSync(SETTINGS_FILE, "utf8")) || {};
	} catch (e) {
		return {};
	}
}

function saveSettings() {
	try {
		fs.writeFileSync(SETTINGS_FILE, JSON.stringify(settings));
	} catch (e) {
		console.error("Can't save settings: " + e.message);
	}
}

function isIPv4(s) {
	return typeof s === "string" && /^\d{1,3}(\.\d{1,3}){3}$/.test(s);
}

function broadcastTargets(broadcast) {
	var targets = ["255.255.255.255"];
	if (isIPv4(broadcast) && broadcast !== targets[0]) targets.unshift(broadcast);
	return targets;
}

/*
 * Sends one UDP datagram to each target with broadcast enabled, then calls onReady(socket, close).
 * close() may be called any number of times (a second socket.close() throws and would kill the
 * service). onError runs at most once, if the socket itself fails.
 */
function broadcast(packet, port, targets, onReady, onError) {
	var socket = dgram.createSocket("udp4");
	var closed = false;
	function close() {
		if (closed) return;
		closed = true;
		try { socket.close(); } catch (ignored) { /* never bound */ }
	}
	socket.on("error", function (e) {
		console.error("UDP error: " + e.message);
		var first = !closed;
		close();
		if (first && onError) onError(e);
	});
	socket.bind(0, function () {
		if (closed) return;
		socket.setBroadcast(true);
		targets.forEach(function (target) {
			// With a callback, a failed send (e.g. network still coming up) is reported here instead of killing the socket.
			socket.send(packet, 0, packet.length, port, target, function (e) {
				if (e) console.error("UDP send to " + target + " failed: " + e.message);
			});
		});
		onReady(socket, close);
	});
}

/* ---------- methods called by the app ---------- */

service.register("configure", function (message) {
	var p = message.payload || {};
	settings.pcId = /^[0-9a-f]{32}$/.test(p.pcId) ? p.pcId : settings.pcId;
	settings.port = (p.port | 0) || 41234;
	settings.mac = /^([0-9a-f]{2}:){5}[0-9a-f]{2}$/i.test(p.mac) ? p.mac : "";
	settings.broadcast = isIPv4(p.broadcast) ? p.broadcast : "255.255.255.255";
	settings.inputAppId = /^com\.webos\.app\.hdmi\d$/.test(p.inputAppId) ? p.inputAppId : "";
	saveSettings();
	dismissed = false;
	startWatcher();
	message.respond({ returnValue: true });
});

service.register("wake", function (message) {
	var p = message.payload || {};
	var mac = p.mac || settings.mac;
	if (!/^([0-9a-f]{2}:){5}[0-9a-f]{2}$/i.test(mac || "")) {
		message.respond({ returnValue: false, errorText: "No MAC address for the PC" });
		return;
	}
	var macBytes = bufferFrom(mac.replace(/:/g, ""), "hex");
	var packet = newBuffer(102);
	packet.fill(0xff, 0, 6);
	for (var i = 0; i < 16; i++) macBytes.copy(packet, 6 + i * 6);
	broadcast(packet, 9, broadcastTargets(p.broadcast || settings.broadcast), function (socket, close) {
		setTimeout(close, 500);
	});
	message.respond({ returnValue: true });
});

/* Broadcasts "MRDISCOVER1"+pcId; the PC answers "MRHERE1"+pcId+port. */
service.register("discover", function (message) {
	var p = message.payload || {};
	var pcId = /^[0-9a-f]{32}$/.test(p.pcId) ? p.pcId : settings.pcId;
	if (!pcId) {
		message.respond({ returnValue: false, errorText: "Not paired" });
		return;
	}
	var request = Buffer.concat([bufferFrom("MRDISCOVER1", "ascii"), bufferFrom(pcId, "hex")]);
	var answered = false;
	function answer(response) {
		if (answered) return;
		answered = true;
		message.respond(response);
	}
	broadcast(request, (p.port | 0) || settings.port || 41234, broadcastTargets(p.broadcast || settings.broadcast), function (socket, close) {
		socket.on("message", function (reply, from) {
			if (reply.length !== 25 || reply.toString("ascii", 0, 7) !== "MRHERE1" || reply.toString("hex", 7, 23) !== pcId) return;
			close();
			answer({ returnValue: true, host: from.address, port: reply[23] | (reply[24] << 8) });
		});
		setTimeout(function () {
			close();
			answer({ returnValue: false, errorText: "No answer from the PC" });
		}, 1500);
	}, function (e) {
		answer({ returnValue: false, errorText: e.message });
	});
});

var closeSubscribers = {};

service.register("close", function (message) {
	if (message.isSubscription) closeSubscribers[message.uniqueToken] = message;
	message.respond({ returnValue: true, subscribed: !!message.isSubscription });
}, function (message) {
	delete closeSubscribers[message.uniqueToken];
});

/* ---------- foreground watcher ---------- */

/* The user closed the overlay on purpose: don't reopen it until they leave the input and come back. */
var dismissed = false;

service.register("dismiss", function (message) {
	dismissed = true;
	message.respond({ returnValue: true });
});

var onPcInput = false;

function onForegroundApp(appId) {
	if (!appId || appId === APP_ID) return;
	if (appId === settings.inputAppId) {
		onPcInput = true;
		if (!dismissed) service.call("luna://com.webos.applicationManager/launch", { id: APP_ID, params: { overlay: true } }, function () {});
		return;
	}
	dismissed = false;
	if (onPcInput) {
		onPcInput = false;
		Object.keys(closeSubscribers).forEach(function (token) {
			closeSubscribers[token].respond({ returnValue: true, close: true });
		});
	}
}

/*
 * The foreground app is only readable through the TV's own SSAP WebSocket API. The first time,
 * the TV asks the user to allow it (READ_RUNNING_APPS only); the client key is kept afterwards.
 */
var ssap = null;
var ssapUseTls = false;
var ssapRetry = 0;
var watcher = { state: "off", detail: "" }; // off | connecting | prompt | ok | error, for the app's status screen

function setWatcher(state, detail) {
	watcher = { state: state, detail: detail || "" };
}

service.register("status", function (message) {
	message.respond({ returnValue: true, watcher: watcher.state, detail: watcher.detail });
});

function startWatcher() {
	if (ssap || ssapRetry || !settings.inputAppId) return;
	var key = crypto.randomBytes(16).toString("base64");
	var options = {
		host: "127.0.0.1",
		port: ssapUseTls ? 3001 : 3000,
		path: "/",
		headers: { Connection: "Upgrade", Upgrade: "websocket", "Sec-WebSocket-Version": "13", "Sec-WebSocket-Key": key },
		rejectUnauthorized: false // the TV's own self-signed certificate, over loopback
	};
	var request = (ssapUseTls ? https : http).request(options);
	ssap = { pending: true };
	if (watcher.state !== "error") setWatcher("connecting");
	request.on("upgrade", function (response, socket) {
		var expected = crypto.createHash("sha1").update(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
		if (response.headers["sec-websocket-accept"] !== expected) {
			socket.destroy();
			retryWatcher();
			return;
		}
		var client = new WebSocketClient(socket, onSsapMessage, retryWatcher);
		ssap = client;
		client.send({ type: "hello", id: "hello", payload: { appId: APP_ID, appName: "Couchpotato", deviceType: "tv", deviceOS: "webOS" } });
		// Some firmware may not answer "hello"; register anyway.
		setTimeout(function () { register(client); }, 2000);
	});
	request.on("response", function (response) {
		response.resume();
		setWatcher("error", "the TV's app API answered HTTP " + response.statusCode);
		ssapUseTls = !ssapUseTls;
		retryWatcher();
	});
	request.on("error", function (e) {
		setWatcher("error", "can't reach the TV's app API: " + e.message);
		ssapUseTls = !ssapUseTls;
		retryWatcher();
	});
	request.setTimeout(5000, function () {
		request.abort();
	});
	request.end();
}

function retryWatcher() {
	ssap = null;
	if (ssapRetry) return;
	ssapRetry = setTimeout(function () {
		ssapRetry = 0;
		startWatcher();
	}, 5000);
}

function register(client) {
	if (client.registerSent || client !== ssap) return;
	client.registerSent = true;
	var payload = { pairingType: "PROMPT", manifest: { permissions: ["READ_RUNNING_APPS"] } };
	if (settings.ssapKey) payload["client-key"] = settings.ssapKey;
	client.send({ type: "register", id: "register", payload: payload });
	if (!settings.ssapKey) setWatcher("prompt");
}

function onSsapMessage(m) {
	switch (m.type) {
		case "hello":
			register(ssap);
			break;
		case "registered":
			if (m.payload && m.payload["client-key"] && m.payload["client-key"] !== settings.ssapKey) {
				settings.ssapKey = m.payload["client-key"];
				saveSettings();
			}
			ssap.send({ type: "subscribe", id: "foreground", uri: "ssap://com.webos.applicationManager/getForegroundAppInfo", payload: {} });
			setWatcher("ok");
			break;
		case "response":
			if (m.id === "foreground" && m.payload) onForegroundApp(m.payload.appId);
			break;
		case "error":
			console.error("SSAP error: " + JSON.stringify(m));
			if (m.id === "register") settings.ssapKey = "";
			setWatcher("error", m.error || "the TV refused the request");
			break;
	}
}

/* Minimal RFC 6455 client: masked text frames out, reassembled text frames in. */
function WebSocketClient(socket, onMessage, onClose) {
	var buffered = newBuffer(0);
	var fragments = [];
	var closed = false;

	function finish() {
		if (closed) return;
		closed = true;
		socket.destroy();
		onClose();
	}

	function write(opcode, payload) {
		var mask = crypto.randomBytes(4);
		var header;
		if (payload.length < 126) {
			header = newBuffer(2);
			header[1] = 0x80 | payload.length;
		} else if (payload.length < 65536) {
			header = newBuffer(4);
			header[1] = 0x80 | 126;
			header.writeUInt16BE(payload.length, 2);
		} else {
			header = newBuffer(10);
			header[1] = 0x80 | 127;
			header.writeUInt32BE(payload.length, 6);
		}
		header[0] = 0x80 | opcode;
		var masked = newBuffer(payload.length);
		for (var i = 0; i < payload.length; i++) masked[i] = payload[i] ^ mask[i & 3];
		socket.write(Buffer.concat([header, mask, masked]));
	}

	socket.setNoDelay(true);
	socket.setTimeout(0);
	socket.on("close", finish);
	socket.on("error", finish);
	socket.on("data", function (chunk) {
		buffered = Buffer.concat([buffered, chunk]);
		while (buffered.length >= 2) {
			var fin = (buffered[0] & 0x80) !== 0;
			var opcode = buffered[0] & 0x0f;
			var length = buffered[1] & 0x7f;
			var offset = 2;
			if (length === 126) {
				if (buffered.length < 4) return;
				length = buffered.readUInt16BE(2);
				offset = 4;
			} else if (length === 127) {
				if (buffered.length < 10) return;
				length = buffered.readUInt32BE(6);
				offset = 10;
			}
			var maskKey = null;
			if (buffered[1] & 0x80) {
				maskKey = buffered.slice(offset, offset + 4);
				offset += 4;
			}
			if (buffered.length < offset + length) return;
			var payload = buffered.slice(offset, offset + length);
			buffered = buffered.slice(offset + length);
			if (maskKey) for (var i = 0; i < payload.length; i++) payload[i] ^= maskKey[i & 3];

			if (opcode === 0x8) {
				finish();
				return;
			} else if (opcode === 0x9) {
				write(0xa, payload);
			} else if (opcode === 0x1 || opcode === 0x0) {
				fragments.push(payload);
				if (fin) {
					var text = Buffer.concat(fragments).toString("utf8");
					fragments = [];
					try {
						onMessage(JSON.parse(text));
					} catch (e) {
						console.error("Bad SSAP message: " + e.message);
					}
				}
			}
		}
	});

	this.send = function (object) {
		if (!closed) write(0x1, bufferFrom(JSON.stringify(object), "utf8"));
	};
}

/* ---------- staying alive ---------- */

/*
 * webOS stops idle services. A persistent activity that calls us every few seconds keeps the
 * watcher running, and also starts it again after the TV boots.
 */
function scheduleActivity(name, type, schedule, method) {
	function create() {
		service.call("luna://com.palm.activitymanager/create", {
			activity: {
				name: name,
				description: "Couchpotato " + method,
				type: type,
				schedule: schedule,
				callback: { method: "luna://" + SERVICE_ID + "/" + method }
			},
			start: true,
			replace: true
		}, function (m) {
			if (m.payload && m.payload.returnValue) adopt();
			else console.error("Can't create activity " + name + ": " + JSON.stringify(m.payload));
		});
	}

	function adopt() {
		var subscription = service.subscribe("luna://com.palm.activitymanager/adopt", {
			activityName: name,
			wait: true,
			subscribe: true,
			detailedEvents: true
		});
		subscription.on("response", function (m) {
			var p = m.payload || {};
			if (!p.returnValue) {
				if (p.errorCode === 2) create(); // no such activity yet
				return;
			}
			if (p.event === "start") service.call("luna://com.palm.activitymanager/complete", { activityId: p.activityId }, function () {});
			else if (p.event === "complete") create();
		});
	}

	adopt();
	return create;
}

service.activityManager.create("CouchpotatoKeepAlive");

var recreateHeartbeat = scheduleActivity("CouchpotatoHeartbeat",
	{ foreground: true, persist: true, explicit: true },
	{ precise: true, interval: "00d00h00m05s" },
	"heartbeat");

service.register("heartbeat", function (message) {
	if (message.activity && message.activity.completed) recreateHeartbeat();
	startWatcher();
	message.respond({ returnValue: true });
});

/* Developer Mode sessions expire; asking the Developer Mode app to extend keeps this app installed. */
var recreateExtend = scheduleActivity("CouchpotatoExtendDevMode",
	{ background: true, persist: true, explicit: true },
	{ interval: "28d00h00m00s" },
	"extend");

service.register("extend", function (message) {
	if (message.activity && message.activity.completed) {
		service.call("luna://com.webos.applicationManager/launch", { id: "com.palmdts.devmode", params: { extend: true } }, function () {});
		recreateExtend();
	}
	message.respond({ returnValue: true });
});

startWatcher();
