/*
 * TV side of the Couchpotato protocol (see docs/DESIGN.md): mutual authentication with the
 * pairing key, then encrypted and authenticated records in both directions.
 */
(function (root, factory) {
	if (typeof module === "object" && module.exports) {
		module.exports = factory(require("./mrcrypto.js"));
	} else {
		root.MRProto = factory(root.MRCrypto);
	}
}(this, function (C) {
	"use strict";

	var MAGIC = new Uint8Array([0x4d, 0x52, 0x01]);
	var TAG_LENGTH = 16;

	var TV = { MOVE: 1, BUTTON: 2, WHEEL: 3, KEY: 4, TEXT: 5, POWER: 6, PING: 7, INFO: 8 };
	var PC = { ACTION: 0x81, PONG: 0x82, STATUS: 0x83 };

	function ascii(s) {
		var out = new Uint8Array(s.length);
		for (var i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
		return out;
	}

	function mac(key, label, parts) {
		return C.hmac(key, C.concat([ascii(label)].concat(parts)));
	}

	function seqBytes(seq, length) {
		var out = new Uint8Array(length);
		var low = seq % 0x100000000, high = Math.floor(seq / 0x100000000);
		var o = length - 8;
		for (var i = 0; i < 4; i++) {
			out[o + i] = (low >>> (8 * i)) & 0xff;
			out[o + 4 + i] = (high >>> (8 * i)) & 0xff;
		}
		return out;
	}

	function Channel(sendEnc, sendMac, recvEnc, recvMac) {
		this.sendEnc = sendEnc; this.sendMac = sendMac; this.sendSeq = 0;
		this.recvEnc = recvEnc; this.recvMac = recvMac; this.recvSeq = 0;
	}

	Channel.prototype.seal = function (plaintext) {
		var ciphertext = C.chacha20(this.sendEnc, seqBytes(this.sendSeq, 12), 0, plaintext);
		var tag = C.hmac(this.sendMac, C.concat([seqBytes(this.sendSeq, 8), ciphertext])).subarray(0, TAG_LENGTH);
		this.sendSeq++;
		return C.concat([ciphertext, tag]);
	};

	/* Returns the plaintext, or null if the record is not authentic. */
	Channel.prototype.open = function (record) {
		if (record.length <= TAG_LENGTH) return null;
		var ciphertext = record.subarray(0, record.length - TAG_LENGTH);
		var tag = C.hmac(this.recvMac, C.concat([seqBytes(this.recvSeq, 8), ciphertext])).subarray(0, TAG_LENGTH);
		if (!C.equal(tag, record.subarray(record.length - TAG_LENGTH))) return null;
		var plaintext = C.chacha20(this.recvEnc, seqBytes(this.recvSeq, 12), 0, ciphertext);
		this.recvSeq++;
		return plaintext;
	};

	function Client(key, tvId) {
		this.key = key;
		this.tvId = tvId;
		this.tvNonce = C.randomBytes(16);
		this.channel = null;
	}

	Client.prototype.hello = function () {
		return C.concat([MAGIC, this.tvId, this.tvNonce]);
	};

	/* Checks the PC's proof. Returns our proof to send back, or null if the PC is not the paired one. */
	Client.prototype.answer = function (challenge) {
		if (challenge.length !== 3 + 16 + 32 || !C.equal(challenge.subarray(0, 3), MAGIC)) return null;
		var pcNonce = challenge.subarray(3, 19);
		var transcript = [this.tvId, this.tvNonce, pcNonce];
		if (!C.equal(mac(this.key, "MR1 pc proof", transcript), challenge.subarray(19))) return null;

		this.channel = new Channel(
			mac(this.key, "MR1 tv>pc enc", transcript), mac(this.key, "MR1 tv>pc mac", transcript),
			mac(this.key, "MR1 pc>tv enc", transcript), mac(this.key, "MR1 pc>tv mac", transcript));
		return mac(this.key, "MR1 tv proof", transcript);
	};

	function u16(n) {
		return [n & 0xff, (n >>> 8) & 0xff];
	}

	function clamp(v, min, max) {
		return v < min ? min : v > max ? max : v;
	}

	var messages = {
		/* x and y are 0..1 across the TV screen */
		move: function (x, y) {
			var px = clamp(Math.round(x * 65536), 0, 65535), py = clamp(Math.round(y * 65536), 0, 65535);
			return new Uint8Array([TV.MOVE].concat(u16(px), u16(py)));
		},
		button: function (button, down) {
			return new Uint8Array([TV.BUTTON, button, down ? 1 : 0]);
		},
		wheel: function (deltaY) {
			return new Uint8Array([TV.WHEEL].concat(u16(clamp(Math.round(deltaY), -32768, 32767) & 0xffff)));
		},
		key: function (code, down) {
			return new Uint8Array([TV.KEY].concat(u16(code), [down ? 1 : 0]));
		},
		/* Long text is split so each record stays small; surrogate pairs are never split. */
		text: function (s) {
			var out = [];
			for (var i = 0; i < s.length;) {
				var end = Math.min(s.length, i + 100);
				var last = s.charCodeAt(end - 1);
				if (end < s.length && last >= 0xd800 && last <= 0xdbff) end--;
				out.push(C.concat([new Uint8Array([TV.TEXT]), C.utf8(s.substring(i, end))]));
				i = end;
			}
			return out;
		},
		power: function (action) {
			return new Uint8Array([TV.POWER, action]);
		},
		ping: function () {
			return new Uint8Array([TV.PING]);
		},
		/* the TV app's version, so the PC can say when it needs updating */
		info: function (version) {
			return C.concat([new Uint8Array([TV.INFO]), C.utf8(String(version).substring(0, 32))]);
		}
	};

	return {
		Client: Client,
		Channel: Channel,
		messages: messages,
		TV: TV,
		PC: PC,
		BUTTON: { LEFT: 0, RIGHT: 1, MIDDLE: 2 },
		ACTION: { KEYBOARD: 1, POWER_MENU: 2 },
		STATUS: { INPUT_BLOCKED: 1 },
		POWER: { SHUTDOWN: 1, SLEEP: 2 }
	};
}));
