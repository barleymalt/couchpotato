/*
 * SHA-256, HMAC-SHA256 and ChaCha20 (RFC 8439) in ES5, because WebCrypto is not available to
 * every webOS version. Verified against the RFC test vectors in tests/js.
 */
(function (root, factory) {
	if (typeof module === "object" && module.exports) {
		module.exports = factory();
	} else {
		root.MRCrypto = factory();
	}
}(this, function () {
	"use strict";

	var K = new Uint32Array([
		0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
		0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
		0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
		0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
		0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
		0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
		0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
		0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
	]);

	function rotr(x, n) {
		return (x >>> n) | (x << (32 - n));
	}

	function sha256(data) {
		var H = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
		var length = data.length;
		var padded = new Uint8Array((length + 9 + 63) & ~63);
		padded.set(data);
		padded[length] = 0x80;
		var bitsHigh = Math.floor(length / 0x20000000);
		var bitsLow = (length << 3) >>> 0;
		var end = padded.length;
		padded[end - 8] = bitsHigh >>> 24; padded[end - 7] = bitsHigh >>> 16; padded[end - 6] = bitsHigh >>> 8; padded[end - 5] = bitsHigh;
		padded[end - 4] = bitsLow >>> 24; padded[end - 3] = bitsLow >>> 16; padded[end - 2] = bitsLow >>> 8; padded[end - 1] = bitsLow;

		var W = new Uint32Array(64);
		for (var offset = 0; offset < end; offset += 64) {
			var i;
			for (i = 0; i < 16; i++) {
				var j = offset + i * 4;
				W[i] = (padded[j] << 24) | (padded[j + 1] << 16) | (padded[j + 2] << 8) | padded[j + 3];
			}
			for (i = 16; i < 64; i++) {
				var s0 = rotr(W[i - 15], 7) ^ rotr(W[i - 15], 18) ^ (W[i - 15] >>> 3);
				var s1 = rotr(W[i - 2], 17) ^ rotr(W[i - 2], 19) ^ (W[i - 2] >>> 10);
				W[i] = W[i - 16] + s0 + W[i - 7] + s1;
			}
			var a = H[0], b = H[1], c = H[2], d = H[3], e = H[4], f = H[5], g = H[6], h = H[7];
			for (i = 0; i < 64; i++) {
				var t1 = (h + (rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + W[i]) | 0;
				var t2 = ((rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
				h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
			}
			H[0] += a; H[1] += b; H[2] += c; H[3] += d; H[4] += e; H[5] += f; H[6] += g; H[7] += h;
		}

		var out = new Uint8Array(32);
		for (var k = 0; k < 8; k++) {
			out[k * 4] = H[k] >>> 24; out[k * 4 + 1] = H[k] >>> 16; out[k * 4 + 2] = H[k] >>> 8; out[k * 4 + 3] = H[k];
		}
		return out;
	}

	function concat(parts) {
		var n = 0, i;
		for (i = 0; i < parts.length; i++) n += parts[i].length;
		var out = new Uint8Array(n), o = 0;
		for (i = 0; i < parts.length; i++) {
			out.set(parts[i], o);
			o += parts[i].length;
		}
		return out;
	}

	function hmac(key, message) {
		if (key.length > 64) key = sha256(key);
		var inner = new Uint8Array(64), outer = new Uint8Array(64);
		for (var i = 0; i < 64; i++) {
			var k = i < key.length ? key[i] : 0;
			inner[i] = k ^ 0x36;
			outer[i] = k ^ 0x5c;
		}
		return sha256(concat([outer, sha256(concat([inner, message]))]));
	}

	function rotl(v, n) {
		return (v << n) | (v >>> (32 - n));
	}

	function quarterRound(x, a, b, c, d) {
		x[a] += x[b]; x[d] = rotl(x[d] ^ x[a], 16);
		x[c] += x[d]; x[b] = rotl(x[b] ^ x[c], 12);
		x[a] += x[b]; x[d] = rotl(x[d] ^ x[a], 8);
		x[c] += x[d]; x[b] = rotl(x[b] ^ x[c], 7);
	}

	function readUint32LE(b, i) {
		return (b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24)) >>> 0;
	}

	/* Returns data XOR the ChaCha20 keystream; data is not modified. */
	function chacha20(key, nonce, counter, data) {
		var state = new Uint32Array(16), x = new Uint32Array(16), i;
		state[0] = 0x61707865; state[1] = 0x3320646e; state[2] = 0x79622d32; state[3] = 0x6b206574;
		for (i = 0; i < 8; i++) state[4 + i] = readUint32LE(key, i * 4);
		state[12] = counter;
		for (i = 0; i < 3; i++) state[13 + i] = readUint32LE(nonce, i * 4);

		var out = new Uint8Array(data.length);
		for (var pos = 0; pos < data.length; pos += 64) {
			x.set(state);
			for (i = 0; i < 10; i++) {
				quarterRound(x, 0, 4, 8, 12); quarterRound(x, 1, 5, 9, 13);
				quarterRound(x, 2, 6, 10, 14); quarterRound(x, 3, 7, 11, 15);
				quarterRound(x, 0, 5, 10, 15); quarterRound(x, 1, 6, 11, 12);
				quarterRound(x, 2, 7, 8, 13); quarterRound(x, 3, 4, 9, 14);
			}
			for (i = 0; i < 16 && pos + i * 4 < data.length; i++) {
				var word = (x[i] + state[i]) >>> 0;
				for (var byteIndex = 0; byteIndex < 4; byteIndex++) {
					var p = pos + i * 4 + byteIndex;
					if (p < data.length) out[p] = data[p] ^ ((word >>> (8 * byteIndex)) & 0xff);
				}
			}
			state[12] += 1;
		}
		return out;
	}

	function randomBytes(n) {
		var b = new Uint8Array(n);
		if (typeof crypto !== "undefined" && crypto.getRandomValues) {
			crypto.getRandomValues(b);
		} else {
			b = new Uint8Array(require("crypto").randomBytes(n));
		}
		return b;
	}

	function equal(a, b) {
		if (a.length !== b.length) return false;
		var diff = 0;
		for (var i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
		return diff === 0;
	}

	function fromHex(s) {
		var out = new Uint8Array(s.length / 2);
		for (var i = 0; i < out.length; i++) out[i] = parseInt(s.substr(i * 2, 2), 16);
		return out;
	}

	function toHex(b) {
		var s = "";
		for (var i = 0; i < b.length; i++) s += (b[i] < 16 ? "0" : "") + b[i].toString(16);
		return s;
	}

	function fromBase64(s) {
		var bin = typeof atob === "function" ? atob(s) : require("buffer").Buffer.from(s, "base64").toString("binary");
		var out = new Uint8Array(bin.length);
		for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
		return out;
	}

	function utf8(s) {
		var bin = unescape(encodeURIComponent(s));
		var out = new Uint8Array(bin.length);
		for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
		return out;
	}

	return {
		sha256: sha256,
		hmac: hmac,
		chacha20: chacha20,
		concat: concat,
		randomBytes: randomBytes,
		equal: equal,
		fromHex: fromHex,
		toHex: toHex,
		fromBase64: fromBase64,
		utf8: utf8
	};
}));
