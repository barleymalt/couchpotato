/*
 * Plays the TV against a real PC server, using the TV app's own protocol code.
 * Usage: node tests/js/interop.js <port> <tvIdHex> <keyBase64>
 * Prints "ok" after a full session, or "refused" if the PC could not prove it holds the key.
 */
"use strict";

const P = require("../../tvapp/app/mrproto.js");
const C = require("../../tvapp/app/mrcrypto.js");

const [port, tvId, key] = process.argv.slice(2);
const client = new P.Client(C.fromBase64(key), C.fromHex(tvId));
const ws = new WebSocket(`ws://127.0.0.1:${port}/`);
ws.binaryType = "arraybuffer";

let channel = null;
let gotAction = false;
let gotPong = false;

const timer = setTimeout(() => fail("timed out"), 5000);

function fail(message) {
	console.error(message);
	process.exit(1);
}

function finish(result) {
	clearTimeout(timer);
	ws.close();
	console.log(result);
	setTimeout(() => process.exit(0), 50);
}

ws.onerror = () => fail("socket error");
ws.onopen = () => ws.send(client.hello());
ws.onmessage = (e) => {
	const data = new Uint8Array(e.data);
	if (!channel) {
		const proof = client.answer(data);
		if (!proof) return finish("refused");
		ws.send(proof);
		channel = client.channel;
		run();
		return;
	}
	const m = channel.open(data);
	if (!m) fail("record failed authentication");
	if (m[0] === P.PC.ACTION && m[1] === P.ACTION.KEYBOARD) gotAction = true;
	if (m[0] === P.PC.PONG) gotPong = true;
	if (gotAction && gotPong) finish("ok");
};

function run() {
	const M = P.messages;
	const send = (m) => ws.send(channel.seal(m));
	send(M.info("9.8.7"));
	send(M.move(0.5, 0.25));
	send(M.button(P.BUTTON.LEFT, true));
	send(M.button(P.BUTTON.LEFT, false));
	send(M.wheel(40));
	send(M.key(13, true));
	send(M.key(13, false));
	M.text("héllo 👋").forEach(send);
	send(M.key(406, true));   // blue: PC answers with the keyboard action
	send(M.key(406, false));
	send(M.key(37, true));    // left arrow left held: the PC must release it when we disconnect
	send(M.ping());
}
