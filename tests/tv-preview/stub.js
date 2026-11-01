/* Fake webOS APIs for tests/tv-preview. Never shipped: the .ipk only contains tvapp/. */
(function () {
	var scenario = (location.search.match(/scenario=([\w-]+)/) || [])[1] || "first-run";
	var pairing = {
		v: 1,
		tvId: "00112233445566778899aabbccddeeff",
		key: btoa(String.fromCharCode.apply(null, new Array(33).join("x").split("").map(function () { return 7; }))),
		pcId: "ffeeddccbbaa99887766554433221100",
		pcName: "DESKTOP-FILIPPO",
		host: "",
		port: 41234,
		mac: "",
		broadcast: "192.168.1.255"
	};

	localStorage.clear();
	var params = {};
	if (scenario === "first-run") params = { pair: pairing };
	if (scenario === "home" || scenario === "connecting") {
		localStorage.setItem("mr.pairing", JSON.stringify(pairing));
		localStorage.setItem("mr.input", JSON.stringify({ id: "HDMI_1", appId: "com.webos.app.hdmi1", label: "HDMI 1" }));
	}
	if (scenario === "connecting" || scenario === "connected") params = { overlay: true };
	if (scenario === "connected") {
		localStorage.setItem("mr.pairing", JSON.stringify(Object.assign({}, pairing, { host: "fake-pc" })));
		localStorage.setItem("mr.input", JSON.stringify({ id: "HDMI_1", appId: "com.webos.app.hdmi1", label: "HDMI 1" }));
		window.WebSocket = FakePcSocket;
	}
	if (scenario === "replace") {
		localStorage.setItem("mr.pairing", JSON.stringify(pairing));
		localStorage.setItem("mr.input", JSON.stringify({ id: "HDMI_1", appId: "com.webos.app.hdmi1", label: "HDMI 1" }));
		params = { pair: { v: 1, tvId: pairing.tvId, key: btoa(new Array(33).join("k")), pcId: pairing.pcId, pcName: "LIVINGROOM-PC", host: "", port: 41234, mac: "", broadcast: "" } };
	}

	window.webOSSystem = {
		launchParams: JSON.stringify(params),
		setCursor: function (c) { window.tvCursor = c === "default" ? "visible" : "hidden"; }
	};

	window.close = function () { window.closedByApp = true; };
	window.lunaCalls = [];

	window.PalmServiceBridge = function () {};
	window.PalmServiceBridge.prototype.call = function (uri, json) {
		var self = this;
		console.log("luna", uri, json);
		window.lunaCalls.push(uri);
		setTimeout(function () {
			var r = { returnValue: true };
			if (uri.indexOf("getAllInputStatus") >= 0) {
				r.devices = [{ id: "HDMI_1", activate: true, label: "HDMI 1" }, { id: "HDMI_2", activate: true, label: "PS5" }, { id: "HDMI_3" }, { id: "HDMI_4" }];
			}
			if (uri.indexOf("/discover") >= 0) r = { returnValue: false, errorText: "No answer from the PC" };
			if (uri.indexOf("/status") >= 0) {
				if (/silent-service/.test(location.search)) return; // &silent-service: the service never answers
				r = { returnValue: true, watcher: "ok" };
			}
			self.onservicecallback(JSON.stringify(r));
		}, 30);
	};

	/*
	 * A pretend PC for scenario=connected: does the PC half of the handshake with the same crypto
	 * code, then decodes everything the TV sends into window.fakePcEvents (e.g. "key 13 down").
	 */
	function FakePcSocket() {
		var self = this, C = window.MRCrypto, P = window.MRProto;
		var key = C.fromBase64(pairing.key), tvId, tvNonce, pcNonce, channel = null;
		window.fakePcEvents = [];
		self.readyState = 0;
		function ascii(s) { return new Uint8Array(s.split("").map(function (c) { return c.charCodeAt(0); })); }
		function mac(label) { return C.hmac(key, C.concat([ascii(label), tvId, tvNonce, pcNonce])); }
		function reply(bytes) { setTimeout(function () { self.onmessage({ data: bytes.buffer }); }, 5); }
		self.send = function (data) {
			if (!tvNonce) {
				tvId = data.subarray(3, 19);
				tvNonce = data.subarray(19, 35);
				pcNonce = C.randomBytes(16);
				reply(C.concat([new Uint8Array([0x4d, 0x52, 1]), pcNonce, mac("MR1 pc proof")]));
			} else if (!channel) {
				channel = new P.Channel(mac("MR1 pc>tv enc"), mac("MR1 pc>tv mac"), mac("MR1 tv>pc enc"), mac("MR1 tv>pc mac"));
			} else {
				var m = channel.open(data);
				var names = { 1: "move", 2: "button", 3: "wheel", 4: "key", 5: "text", 6: "power", 7: "ping", 8: "info" };
				if (m[0] === 4) window.fakePcEvents.push("key " + (m[1] | m[2] << 8) + (m[3] ? " down" : " up"));
				else if (m[0] === 2) window.fakePcEvents.push("button " + m[1] + (m[2] ? " down" : " up"));
				else if (m[0] === 8) window.fakePcEvents.push("info " + new TextDecoder().decode(m.subarray(1)));
				else if (m[0] !== 1 && m[0] !== 7) window.fakePcEvents.push(names[m[0]]);
				if (m[0] === 7) reply(channel.seal(new Uint8Array([0x82])));
			}
		};
		// window.fakePcStatus("text") sends the "input blocked" status with an explanation, like the PC does.
		window.fakePcStatus = function (text) {
			reply(channel.seal(C.concat([new Uint8Array([0x83, 1]), text ? C.utf8(text) : new Uint8Array(0)])));
		};
		self.close = function () {
			self.readyState = 3;
			setTimeout(function () { self.onclose(); }, 0);
		};
		setTimeout(function () { self.readyState = 1; self.onopen(); }, 50);
	}

	// Stand-in for the HDMI picture under the transparent app.
	document.addEventListener("DOMContentLoaded", function () {
		document.documentElement.style.background = "linear-gradient(135deg, #2d6a4f 0%, #1b4965 60%, #0b132b 100%)";
	});
}());
