/*
 * Checks the TV's pure-JS crypto against RFC vectors and against Node's OpenSSL-backed crypto.
 * Usage: node tests/js/crypto.test.js
 */
"use strict";

const nodeCrypto = require("crypto");
const assert = require("assert");
const C = require("../../tvapp/app/mrcrypto.js");
const P = require("../../tvapp/app/mrproto.js");

const hex = (b) => Buffer.from(b).toString("hex");
const bytes = (s) => new Uint8Array(Buffer.from(s));

assert.strictEqual(hex(C.sha256(bytes(""))), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
assert.strictEqual(hex(C.sha256(bytes("abc"))), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
assert.strictEqual(hex(C.hmac(bytes("Jefe"), bytes("what do ya want for nothing?"))),
	"5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"); // RFC 4231 case 2

const rfcKey = new Uint8Array(32).map((_, i) => i);
const rfcPlain = bytes("Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
assert.strictEqual(hex(C.chacha20(rfcKey, C.fromHex("000000000000004a00000000"), 1, rfcPlain)),
	"6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0bf91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d807ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab77937365af90bbf74a35be6b40b8eedf2785e42874d");

for (let n = 0; n < 300; n++) {
	const data = new Uint8Array(nodeCrypto.randomBytes(n));
	const key = new Uint8Array(nodeCrypto.randomBytes(n % 100 + 1));
	assert.strictEqual(hex(C.sha256(data)), nodeCrypto.createHash("sha256").update(data).digest("hex"), "sha256 length " + n);
	assert.strictEqual(hex(C.hmac(key, data)), nodeCrypto.createHmac("sha256", key).update(data).digest("hex"), "hmac length " + n);

	const k = new Uint8Array(nodeCrypto.randomBytes(32));
	const nonce = new Uint8Array(nodeCrypto.randomBytes(12));
	const counter = nodeCrypto.randomBytes(4).readUInt32LE(0) >>> 8; // stay clear of 32-bit wrap
	const iv = Buffer.concat([Buffer.from([counter & 255, (counter >>> 8) & 255, (counter >>> 16) & 255, counter >>> 24]), Buffer.from(nonce)]);
	const expected = nodeCrypto.createCipheriv("chacha20", Buffer.from(k), iv).update(data);
	assert.strictEqual(hex(C.chacha20(k, nonce, counter, data)), expected.toString("hex"), "chacha20 length " + n);
}

// Records survive a round trip and the text helper never splits a surrogate pair.
const a = new P.Channel(new Uint8Array(32).fill(1), new Uint8Array(32).fill(2), new Uint8Array(32).fill(3), new Uint8Array(32).fill(4));
const b = new P.Channel(new Uint8Array(32).fill(3), new Uint8Array(32).fill(4), new Uint8Array(32).fill(1), new Uint8Array(32).fill(2));
for (let i = 0; i < 3; i++) assert.strictEqual(hex(b.open(a.seal(new Uint8Array([i, 42])))), "0" + i + "2a");
const long = "x".repeat(99) + "😀" + "y".repeat(250);
const parts = P.messages.text(long);
assert.strictEqual(parts.map((m) => Buffer.from(m.subarray(1)).toString("utf8")).join(""), long);
assert.ok(parts.every((m) => m.length <= 513));

console.log("ok");
