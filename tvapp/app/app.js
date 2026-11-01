/*
 * Couchpotato TV app. Runs as a transparent window on top of the PC's HDMI input, forwards the
 * Magic Remote to the PC, and gets out of the way when the user switches to anything else.
 */
(function () {
	"use strict";

	var APP_ID = "com.couchpotato.tv";
	var SERVICE = "luna://" + APP_ID + ".service/";
	var LONG_PRESS_MS = 600;
	var DRAG_THRESHOLD_PX = 12;
	var PING_MS = 10000;
	var PONG_TIMEOUT_MS = 5000;
	var HANDSHAKE_TIMEOUT_MS = 4000;
	var WAKE_AFTER_MS = 2500;
	var GIVE_UP_MS = 120000; // a PC cold-booting after Wake-on-LAN can take a while
	var BACK_HOLD_MS = 1000; // holding Back leaves Couchpotato and gives the remote back to the TV
	var BACK = 461;
	var CURSOR_KEYS = { 1536: true, 1537: true }; // pointer shown/hidden notifications on old webOS

	var P = window.MRProto, C = window.MRCrypto, M = P.messages;

	/* ---------- webOS plumbing ---------- */

	var pendingCalls = [];

	function luna(uri, params, onResponse) {
		var bridge = new PalmServiceBridge();
		var subscription = !!(params && params.subscribe);
		pendingCalls.push(bridge);
		bridge.onservicecallback = function (text) {
			var response;
			try {
				response = JSON.parse(text);
			} catch (e) {
				response = { returnValue: false, errorText: String(text) };
			}
			if (!subscription) {
				var i = pendingCalls.indexOf(bridge);
				if (i >= 0) pendingCalls.splice(i, 1);
			}
			if (onResponse) onResponse(response);
		};
		bridge.call(uri, JSON.stringify(params || {}));
	}

	function system() {
		return window.webOSSystem || window.PalmSystem || null;
	}

	function launchParams() {
		var s = system();
		try {
			return JSON.parse((s && s.launchParams) || "{}") || {};
		} catch (e) {
			return {};
		}
	}

	var appUrl = document.baseURI || location.href;
	var appRoot = appUrl.substring(0, appUrl.lastIndexOf("/") + 1);

	function showTvCursor(show) {
		var s = system();
		if (!s || !s.setCursor) return;
		try {
			if (show) s.setCursor("default");
			else s.setCursor(appRoot + "cursor.png", 0, 0);
		} catch (e) { /* older firmware without cursor control */ }
	}

	/* This app's version, from its manifest; the PC compares it with its own. */
	var appVersion = "";
	(function readVersion() {
		var xhr = new XMLHttpRequest();
		xhr.onload = function () {
			try {
				appVersion = String(JSON.parse(xhr.responseText).version || "");
			} catch (e) { /* stays unknown */ }
			if (connected && appVersion) send(M.info(appVersion));
			if (dialogEl && dialogEl.isHome) dialogEl.querySelector("h1").textContent = "Couchpotato " + appVersion;
		};
		xhr.open("GET", appRoot + "appinfo.json", true);
		xhr.send(null);
	}());

	/* ---------- settings ---------- */

	function load(name) {
		try {
			return JSON.parse(localStorage.getItem("mr." + name));
		} catch (e) {
			return null;
		}
	}

	function save(name, value) {
		localStorage.setItem("mr." + name, JSON.stringify(value));
	}

	var pairing = load("pairing"); // { tvId, key, pcId, pcName, host, port, mac, broadcast }
	var input = load("input");     // { id, appId, label }

	function acceptPairing(p) {
		var hex32 = /^[0-9a-f]{32}$/;
		if (!p || p.v !== 1 || !hex32.test(p.tvId) || !hex32.test(p.pcId) || typeof p.key !== "string" || C.fromBase64(p.key).length !== 32) return false;
		pairing = {
			tvId: p.tvId,
			key: p.key,
			pcId: p.pcId,
			pcName: String(p.pcName || "PC"),
			host: String(p.host || ""),
			port: (p.port | 0) || 41234,
			mac: String(p.mac || ""),
			broadcast: String(p.broadcast || "255.255.255.255")
		};
		save("pairing", pairing);
		return true;
	}

	/* The background service only gets what it needs to wake and find the PC: never the key. */
	function syncService() {
		if (!pairing) return;
		luna(SERVICE + "configure", {
			pcId: pairing.pcId,
			port: pairing.port,
			mac: pairing.mac,
			broadcast: pairing.broadcast,
			inputAppId: input ? input.appId : ""
		});
	}

	/* ---------- UI ---------- */

	var toastEl = document.getElementById("toast");
	var toastTimer = 0;
	var dialogEl = null;
	var dialogOnBack = null;

	function toast(text, ms) {
		toastEl.textContent = text || "";
		toastEl.className = text ? "toast visible" : "toast";
		clearTimeout(toastTimer);
		if (text && ms) toastTimer = setTimeout(function () { toast(""); }, ms);
	}

	/* buttons: [{ label, action }]; the first one gets the focus. Back runs onBack, or just closes. */
	function dialog(title, text, buttons, onBack) {
		closeDialog();
		var backdrop = document.createElement("div");
		backdrop.className = "backdrop";
		var box = document.createElement("div");
		box.className = "dialog";
		var h = document.createElement("h1");
		h.textContent = title;
		box.appendChild(h);
		if (text) {
			var p = document.createElement("p");
			p.textContent = text;
			box.appendChild(p);
		}
		var row = document.createElement("div");
		row.className = "buttons";
		buttons.forEach(function (b) {
			var el = document.createElement("button");
			el.textContent = b.label;
			el.addEventListener("click", function () {
				closeDialog();
				if (b.action) b.action();
			});
			row.appendChild(el);
		});
		box.appendChild(row);
		backdrop.appendChild(box);
		document.body.appendChild(backdrop);
		dialogEl = backdrop;
		dialogOnBack = onBack || null;
		if (row.firstChild) row.firstChild.focus();
		updateCapture();
	}

	function closeDialog() {
		if (!dialogEl) return;
		dialogEl.parentNode.removeChild(dialogEl);
		dialogEl = null;
		dialogOnBack = null;
		updateCapture();
	}

	function dialogKey(e) {
		var buttons = dialogEl.querySelectorAll("button");
		var i = Array.prototype.indexOf.call(buttons, document.activeElement);
		switch (e.keyCode) {
			case 37: case 38:
				buttons[(i - 1 + buttons.length) % buttons.length].focus();
				break;
			case 39: case 40:
				buttons[(i + 1) % buttons.length].focus();
				break;
			case 461:
				var onBack = dialogOnBack;
				closeDialog();
				if (onBack) onBack();
				break;
			default:
				return;
		}
		e.preventDefault();
	}

	/* ---------- connection ---------- */

	var overlay = false;
	var socket = null, client = null, channel = null, connected = false;
	var retryTimer = 0, pingTimer = 0, pongTimer = 0, handshakeTimer = 0;
	var failingSince = 0, wakeSent = false, refusals = 0, lastDiscovery = 0;
	var candidate = null; // address from a discovery reply: anyone can answer, so it's saved only after the PC proves itself

	function connect() {
		clearTimeout(retryTimer);
		if (!pairing || socket || !overlay || document.hidden) return;
		var target = candidate || pairing;
		if (!target.host) {
			onSocketGone(false);
			return;
		}
		var ws, opened = false;
		client = new P.Client(C.fromBase64(pairing.key), C.fromHex(pairing.tvId));
		try {
			ws = new WebSocket("ws://" + target.host + ":" + target.port + "/");
		} catch (e) {
			onSocketGone(false);
			return;
		}
		ws.binaryType = "arraybuffer";
		ws.target = target;
		socket = ws;
		handshakeTimer = setTimeout(function () {
			if (ws === socket && !connected) ws.close();
		}, HANDSHAKE_TIMEOUT_MS);
		ws.onopen = function () {
			opened = true;
			ws.send(client.hello());
		};
		ws.onmessage = function (e) {
			if (ws === socket) onData(new Uint8Array(e.data));
		};
		ws.onclose = function () {
			if (ws !== socket) return;
			if (ws.target === candidate && !connected) candidate = null;
			// Reached the PC but it hung up before trusting us: the pairing is gone on the PC side.
			// An impostor (wrong proof) is not that: keep trying the real PC.
			onSocketGone(opened && !connected && !ws.impostor);
		};
	}

	function disconnect() {
		var ws = socket;
		socket = null;
		if (ws) ws.close();
		onSocketGone(false);
	}

	function onData(data) {
		if (!channel) {
			var proof = client.answer(data);
			if (!proof) {
				console.warn("Something at " + socket.target.host + " pretended to be " + pairing.pcName);
				socket.impostor = true;
				socket.close();
				return;
			}
			socket.send(proof);
			channel = client.channel;
			onConnected();
			return;
		}
		var m = channel.open(data);
		if (!m) {
			socket.close();
			return;
		}
		switch (m[0]) {
			case P.PC.PONG:
				clearTimeout(pongTimer);
				break;
			case P.PC.ACTION:
				if (m[1] === P.ACTION.KEYBOARD) toggleKeyboard();
				else if (m[1] === P.ACTION.POWER_MENU) powerMenu();
				break;
			case P.PC.STATUS:
				// The PC says why when it knows (e.g. which app runs as administrator, and how to fix it).
				if (m[1] === P.STATUS.INPUT_BLOCKED) toast(utf8Decode(m.subarray(2)) ||
					"Windows is blocking the remote: " + pairing.pcName + " is locked or showing a permission prompt. Use the PC's mouse or keyboard for that.", 10000);
				break;
		}
	}

	function utf8Decode(bytes) {
		var binary = "";
		for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
		try {
			return decodeURIComponent(escape(binary));
		} catch (e) {
			return "";
		}
	}

	function send(message) {
		if (connected && socket && socket.readyState === 1) socket.send(channel.seal(message));
	}

	function onConnected() {
		connected = true;
		failingSince = 0;
		wakeSent = false;
		refusals = 0;
		clearTimeout(handshakeTimer);
		if (appVersion) send(M.info(appVersion));
		if (socket.target === candidate) {
			pairing.host = candidate.host;
			pairing.port = candidate.port;
			save("pairing", pairing);
			candidate = null;
		}
		// Nobody reads manuals: the first few times, say how to get the remote back for the TV.
		var hints = load("hints") || 0;
		if (hints < 5) {
			save("hints", hints + 1);
			toast("Connected to " + pairing.pcName + ". Hold Back to use the TV's own menus.", 6000);
		} else {
			toast("Connected to " + pairing.pcName, 2500);
		}
		pingTimer = setInterval(function () {
			send(M.ping());
			clearTimeout(pongTimer);
			pongTimer = setTimeout(function () {
				if (socket) socket.close();
			}, PONG_TIMEOUT_MS);
		}, PING_MS);
		updateCapture();
	}

	function onSocketGone(refused) {
		var wasConnected = connected;
		socket = null;
		channel = null;
		connected = false;
		clearTimeout(handshakeTimer);
		clearInterval(pingTimer);
		clearTimeout(pongTimer);
		clearTimeout(backTimer);
		backTimer = 0;
		releasePress();
		keysDown = {}; // the PC releases whatever was held when a connection drops
		if (keyboardOpen) kbd.blur();
		updateCapture();
		if (!overlay || document.hidden) return;

		if (refused && ++refusals >= 3) {
			toast("");
			dialog(pairing.pcName + " doesn't recognise this TV",
				"On the PC, open Couchpotato and choose \"Set up TV\" to pair again.",
				[{ label: "Try again", action: retry }, { label: "Close", action: closeApp }], closeApp);
			return;
		}

		var now = Date.now();
		if (!failingSince) failingSince = now;
		if (wasConnected) toast("Lost the connection to " + pairing.pcName + ". Reconnecting…", 0);
		else if (!wakeSent) toast("Connecting to " + pairing.pcName + "…", 0);

		if (now - failingSince > GIVE_UP_MS) {
			toast("");
			dialog("Can't reach " + pairing.pcName,
				"Check that the PC is on and Couchpotato is running on it. To turn the PC on from the TV, enable Wake-on-LAN in its settings.",
				[{ label: "Try again", action: retry }, { label: "Close", action: closeApp }], closeApp);
			return;
		}
		if (!wasConnected && !wakeSent && now - failingSince >= WAKE_AFTER_MS) {
			wakeSent = true;
			wake();
			toast("Waking up " + pairing.pcName + "…", 0);
		}
		if (now - lastDiscovery > 3000) discover();
		retryTimer = setTimeout(connect, 1500);
	}

	function retry() {
		failingSince = 0;
		wakeSent = false;
		refusals = 0;
		toast("Connecting to " + pairing.pcName + "…", 0);
		connect();
	}

	/* Asks the service to broadcast for the PC, in case its IP address changed. */
	function discover() {
		lastDiscovery = Date.now();
		luna(SERVICE + "discover", { pcId: pairing.pcId, port: pairing.port, broadcast: pairing.broadcast }, function (r) {
			if (!r.returnValue || !r.host || (r.host === pairing.host && r.port === pairing.port)) return;
			candidate = { host: r.host, port: r.port || pairing.port };
			connect();
		});
	}

	function wake() {
		luna(SERVICE + "wake", { mac: pairing.mac, broadcast: pairing.broadcast, host: pairing.host });
	}

	/* Closing on the user's request also tells the service not to reopen us until they come back to the input. */
	function closeApp() {
		exitApp(true);
	}

	/* Always hands the TV pointer back before the window goes away. */
	function exitApp(dismiss) {
		showTvCursor(true);
		if (dismiss) luna(SERVICE + "dismiss", {});
		setTimeout(function () { window.close(); }, 100);
	}

	window.addEventListener("unload", function () {
		showTvCursor(true);
	});

	/* ---------- remote → PC ---------- */

	var surface = document.getElementById("surface");
	var kbd = document.getElementById("kbd");
	var keyboardOpen = false;
	var press = null; // left button held: { x, y, timer, dragging }
	var focused = true; // launched apps have the focus; blur/focus events track it from here
	var backTimer = 0;

	/*
	 * The TV's own menus (Home, settings, input list) open on top of us and take the focus. While
	 * that happens the remote belongs to the TV: show its pointer and forward nothing.
	 */
	function capturing() {
		return connected && overlay && focused && !dialogEl && !keyboardOpen;
	}

	function updateCapture() {
		showTvCursor(!capturing());
	}

	window.addEventListener("blur", function () {
		focused = false;
		releaseHeld();
		updateCapture();
	});

	window.addEventListener("focus", function () {
		focused = true;
		updateCapture();
	});

	/* Lets go of everything the PC thinks is held down. */
	function releaseHeld() {
		if (press && press.dragging) send(M.button(P.BUTTON.LEFT, false));
		releasePress();
		clearTimeout(backTimer);
		backTimer = 0;
		for (var code in keysDown) {
			if (keysDown.hasOwnProperty(code)) send(M.key(+code, false));
		}
		keysDown = {};
	}

	function sendPointer(e) {
		send(M.move(e.clientX / window.innerWidth, e.clientY / window.innerHeight));
	}

	surface.addEventListener("mousemove", function (e) {
		if (!capturing()) return;
		sendPointer(e);
		if (press && press.timer && Math.abs(e.clientX - press.x) + Math.abs(e.clientY - press.y) > DRAG_THRESHOLD_PX) {
			clearTimeout(press.timer);
			press.timer = 0;
			press.dragging = true;
			// Press where the drag started (a window edge is only a few pixels wide), then catch up.
			send(M.move(press.x / window.innerWidth, press.y / window.innerHeight));
			send(M.button(P.BUTTON.LEFT, true));
			sendPointer(e);
		}
	});

	/* Click = short press. Moving while pressed drags. Holding still for LONG_PRESS_MS = right click. */
	surface.addEventListener("mousedown", function (e) {
		if (!capturing() || e.button !== 0) return;
		e.preventDefault();
		sendPointer(e);
		var p = { x: e.clientX, y: e.clientY, dragging: false, timer: 0 };
		p.timer = setTimeout(function () {
			p.timer = 0;
			send(M.button(P.BUTTON.RIGHT, true));
			send(M.button(P.BUTTON.RIGHT, false));
		}, LONG_PRESS_MS);
		press = p;
	});

	surface.addEventListener("mouseup", function () {
		if (!press) return;
		if (press.timer) {
			clearTimeout(press.timer);
			send(M.button(P.BUTTON.LEFT, true));
			send(M.button(P.BUTTON.LEFT, false));
		} else if (press.dragging) {
			send(M.button(P.BUTTON.LEFT, false));
		}
		press = null;
	});

	function releasePress() {
		if (press) clearTimeout(press.timer);
		press = null;
	}

	surface.addEventListener("wheel", function (e) {
		if (!capturing()) return;
		e.preventDefault();
		var scale = e.deltaMode === 1 ? 40 : e.deltaMode === 2 ? 800 : 1;
		send(M.wheel(e.deltaY * scale));
	});

	document.addEventListener("keydown", function (e) {
		var code = e.keyCode;
		if (dialogEl) {
			dialogKey(e);
			return;
		}
		if (!overlay) {
			if (code === BACK) {
				e.preventDefault();
				closeApp();
			}
			return;
		}
		if (CURSOR_KEYS[code]) return;
		if (keyboardOpen && code === BACK) {
			e.preventDefault();
			kbd.blur();
			return;
		}
		if (!connected) {
			if (code === BACK) {
				e.preventDefault();
				closeApp();
			} else if (code === 403) {
				wake();
				retry();
			}
			return;
		}
		e.preventDefault();
		if (code === BACK) {
			// Sent on release, so a long press can mean "leave" instead.
			if (!backTimer) {
				backTimer = setTimeout(function () {
					backTimer = 0;
					toast("Leaving Couchpotato. Switch to " + pairing.pcName + "'s input again to come back.", 3000);
					closeApp();
				}, BACK_HOLD_MS);
			}
			return;
		}
		keysDown[code] = true;
		send(M.key(code, true));
	}, true);

	/* Every key the PC saw go down also goes up, even if a dialog opened in between (e.g. the power menu). */
	var keysDown = {};

	document.addEventListener("keyup", function (e) {
		if (e.keyCode === BACK && backTimer) {
			clearTimeout(backTimer);
			backTimer = 0;
			e.preventDefault();
			send(M.key(BACK, true));
			send(M.key(BACK, false));
			return;
		}
		if (!keysDown[e.keyCode]) return;
		delete keysDown[e.keyCode];
		e.preventDefault();
		send(M.key(e.keyCode, false));
	}, true);

	function toggleKeyboard() {
		if (keyboardOpen) kbd.blur();
		else kbd.focus();
	}

	kbd.addEventListener("focus", function () {
		keyboardOpen = true;
		updateCapture();
	});
	kbd.addEventListener("blur", function () {
		keyboardOpen = false;
		updateCapture();
	});
	kbd.addEventListener("input", function () {
		var text = kbd.value;
		kbd.value = "";
		if (text) M.text(text).forEach(send);
	});

	function powerMenu() {
		dialog("Turn off " + pairing.pcName + "?", "", [
			{ label: "Cancel" },
			{ label: "Sleep", action: function () { send(M.power(P.POWER.SLEEP)); } },
			{ label: "Shut down", action: function () { send(M.power(P.POWER.SHUTDOWN)); } }
		]);
	}

	/* ---------- flows ---------- */

	function inputLabel(d) {
		return d.label || d.deviceName || d.id.replace("_", " ");
	}

	/* Picks the HDMI input the PC is on. If exactly one HDMI input has a signal, that's it. */
	function chooseInput(then, alwaysAsk) {
		luna("luna://com.webos.service.eim/getAllInputStatus", {}, function (r) {
			var devices = ((r && r.devices) || []).filter(function (d) { return /^HDMI_\d$/.test(d.id); });
			if (!devices.length) devices = [{ id: "HDMI_1" }, { id: "HDMI_2" }, { id: "HDMI_3" }, { id: "HDMI_4" }];
			var active = devices.filter(function (d) { return d.activate; });

			function pick(d) {
				input = { id: d.id, appId: "com.webos.app.hdmi" + d.id.split("_")[1], label: inputLabel(d) };
				save("input", input);
				syncService();
				then();
			}

			if (active.length === 1 && !alwaysAsk) {
				pick(active[0]);
				toast("Using " + input.label + " for " + pairing.pcName + ". Open the Couchpotato app to change it.", 6000);
				return;
			}
			dialog("Which input is " + pairing.pcName + " connected to?",
				"Inputs marked ● have a signal right now. Couchpotato will start by itself whenever you switch to the PC's input.",
				devices.map(function (d) {
					return { label: inputLabel(d) + (d.activate ? " ●" : ""), action: function () { pick(d); } };
				}), closeApp);
		});
	}

	function startOverlay(switchToInput) {
		if (!overlay) {
			overlay = true;
			luna(SERVICE + "close", { subscribe: true }, function (r) {
				if (r.close) exitApp(false);
			});
		}
		if (switchToInput) luna("luna://com.webos.applicationManager/launch", { id: input.appId });
		if (connected || socket) return; // relaunched while already connected or connecting
		closeDialog();
		retry();
	}

	/*
	 * Shown at once (never wait on the service: an invisible window would swallow the remote),
	 * then says whether opening by itself works, which depends on the one-time permission prompt.
	 */
	function home() {
		var paired = "Paired with " + pairing.pcName + " on " + input.label + ". ";
		var hint = "Hold Back to leave it and use the TV.";
		dialog("Couchpotato" + (appVersion ? " " + appVersion : ""), paired + hint, [
			{ label: "Go to " + pairing.pcName, action: function () { startOverlay(true); } },
			{ label: "Change input", action: function () { chooseInput(home, true); } },
			{ label: "Close", action: closeApp }
		], closeApp);
		var shown = dialogEl;
		shown.isHome = true;
		luna(SERVICE + "status", {}, function (s) {
			if (dialogEl !== shown) return;
			var auto = s.watcher === "ok"
				? "It opens by itself when you switch to " + input.label + ". "
				: s.watcher === "prompt"
					? "To open by itself, it needs the permission the TV is asking for. "
					: "It can't open by itself yet" + (s.detail ? " (" + s.detail + ")" : "") + ". Restart the TV and allow Couchpotato when asked. ";
			shown.querySelector("p").textContent = paired + auto + hint;
		});
	}

	/* The app's tile was chosen while we were already running in the background: take the user to the PC. */
	function goToPc() {
		toast("Going to " + pairing.pcName + ". Hold Back to come back to the TV.", 4000);
		startOverlay(true);
	}

	/*
	 * Any app on the TV can launch us with parameters, so an existing pairing is only replaced
	 * after the user confirms on the TV. A fresh install has nothing to protect yet.
	 */
	function route(params) {
		var p = params.pair;
		if (p && pairing && p.key !== pairing.key) {
			var name = String(p.pcName || "a new PC");
			dialog("Pair with " + name + "?",
				"This replaces the pairing with " + pairing.pcName + ". Choose Replace only if you just ran \"Set up a TV\" on " + name + ".",
				[
					{ label: "Keep current", action: function () { proceed({}); } },
					{ label: "Replace", action: function () { pair(p); proceed(params); } }
				], function () { proceed({}); });
			return;
		}
		if (p) pair(p);
		proceed(params);
	}

	function pair(p) {
		if (acceptPairing(p)) toast("Paired with " + pairing.pcName, 4000);
		else toast("The pairing data from the PC was invalid. Run \"Set up a TV\" again.", 8000);
	}

	function proceed(params) {
		if (!pairing) {
			dialog("Couchpotato isn't set up yet",
				"On your PC, open Couchpotato and choose \"Set up TV\". It installs and pairs this app for you.",
				[{ label: "Close", action: closeApp }], closeApp);
			return;
		}
		syncService();
		if (!input) chooseInput(function () { startOverlay(true); });
		else if (params.overlay) startOverlay(false);
		else if (params.pair) startOverlay(true);
		else if (overlay) goToPc();
		else home();
	}

	/* Every launch must end with something on screen; a bare transparent window would trap the remote. */
	function launched(params) {
		focused = true;
		route(params);
		updateCapture();
		setTimeout(function () {
			if (!overlay && !dialogEl) exitApp(false);
		}, 5000);
	}

	document.addEventListener("webOSRelaunch", function () {
		launched(launchParams());
	});

	document.addEventListener(typeof document.hidden !== "undefined" ? "visibilitychange" : "webkitvisibilitychange", function () {
		if (document.hidden || document.webkitHidden) disconnect();
		else if (overlay) retry();
	});

	window.addEventListener("error", function (e) {
		toast("Couchpotato error: " + e.message, 8000);
	});

	launched(launchParams());
}());
