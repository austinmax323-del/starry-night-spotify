// hide-npv.js — close the right-hand "Now Playing" panel on startup.
// Spotify rewrites ui.right_sidebar_content to "now_playing_view" on every
// launch, so closing the panel by hand never persists. This clicks the
// now-playing-bar cover art (the panel toggle) once the UI is up, and only
// while the panel is actually open, so manually reopening it later still works.
//
// State is read from the ui.right_sidebar_content localStorage key, NOT from
// the panel's width: the panel element keeps its 355px box after closing, so a
// width check reads "open" on an already-closed panel and toggles it back on.
(function hideNowPlayingViewOnStartup() {
	const STATE_KEY_SUFFIX = ":ui.right_sidebar_content";
	const CLOSED = '"disabled"';

	const stateKey = () => Object.keys(localStorage).find((k) => k.endsWith(STATE_KEY_SUFFIX));

	const isOpen = () => {
		const key = stateKey();
		return key ? localStorage.getItem(key) !== CLOSED : false;
	};

	let waits = 0;
	let clicks = 0;

	const tick = () => {
		const toggle = document.querySelector('[data-testid="cover-art-button"]');
		if (!toggle || !stateKey()) {
			if (waits++ > 40) return; // give up after ~20s rather than fight the UI
			setTimeout(tick, 500);
			return;
		}

		if (!isOpen()) return; // hidden — done
		if (clicks++ >= 3) return; // never thrash the toggle

		toggle.click();
		setTimeout(tick, 800); // re-check; only clicks again if it is still open
	};

	setTimeout(tick, 1000);
})();
