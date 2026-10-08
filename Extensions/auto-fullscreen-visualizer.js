// auto-fullscreen-visualizer.js — when playback starts, open the backmusic
// visualizer and put it in fullscreen.
//
// backmusic's fullscreen is internal React state (a portal + .bm-root--fs
// class), not document.requestFullscreen, so there is no API to call — the only
// handle is its own toggle button. Note that .bm-root keeps its box after
// closing, so open/closed is read from the --fs class, never from a width.
//
// Fullscreen here means "fills the Spotify window", not macOS fullscreen.
//
// Only fires on the transition into playing, never on a timer, so pressing Esc
// to leave fullscreen mid-song does not get undone.
(function autoFullscreenVisualizer() {
	const ROUTE = "/backmusic";
	const FS_BUTTON = 'button.controls__btn[title="Fullscreen"]';

	const isFullscreen = () => !!document.querySelector(".bm-root--fs");

	const goFullscreen = () => {
		if (isFullscreen()) return;

		const history = Spicetify.Platform?.History;
		if (history && history.location.pathname !== ROUTE) history.push(ROUTE);

		// The app mounts a frame or two after the route change; poll briefly.
		let tries = 0;
		const waitForButton = () => {
			if (isFullscreen()) return;
			if (tries++ > 20) return; // ~5s, then give up quietly

			const button = document.querySelector(FS_BUTTON);
			if (button) button.click();
			else setTimeout(waitForButton, 250);
		};
		setTimeout(waitForButton, 250);
	};

	let wasPlaying = false;
	const onPlaybackChange = () => {
		const playing = !!Spicetify.Player?.isPlaying?.();
		if (playing && !wasPlaying) goFullscreen();
		wasPlaying = playing;
	};

	const start = () => {
		if (!Spicetify.Player?.addEventListener || !Spicetify.Platform?.History) {
			setTimeout(start, 500);
			return;
		}

		Spicetify.Player.addEventListener("onplaypause", onPlaybackChange);
		onPlaybackChange(); // already playing at startup
	};

	start();
})();
