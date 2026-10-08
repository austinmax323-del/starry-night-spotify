// visualizer-button.js — put the backmusic visualizer button on the right of the
// search bar, mirroring the Home button on its left, and hide the original
// nav icon Spicetify puts in the left-hand cluster. Nothing opens by itself;
// the visualizer only appears when this button is clicked.
(function visualizerButton() {
	const ROUTE = "/backmusic";
	const ID = "visualizer-search-button";
	const ICON =
		"<svg role='img' width='100%' height='100%' viewBox='0 0 16 16' fill='currentColor'>" +
		"<rect x='2.6' y='5' width='1.8' height='6' rx='0.9'/><rect x='5.6' y='2' width='1.8' height='12' rx='0.9'/>" +
		"<rect x='8.6' y='4' width='1.8' height='8' rx='0.9'/><rect x='11.6' y='2.5' width='1.8' height='11' rx='0.9'/></svg>";

	const style = document.createElement("style");
	style.textContent = `button.custom-navlink[aria-label="backmusic"] { display: none !important; }`;
	document.head.appendChild(style);

	const syncActive = (button) => {
		const active = Spicetify.Platform?.History?.location?.pathname === ROUTE;
		button.classList.toggle("main-globalNav-navLinkActive", active);
	};

	const ensureButton = () => {
		const container = document.querySelector(".main-globalNav-searchContainer");
		const home = container?.querySelector('button[aria-label="Home"]');
		if (!container || !home || container.querySelector(`#${ID}`)) return;

		const button = document.createElement("button");
		button.id = ID;
		button.className = home.className.replace("main-globalNav-navLinkActive", "").trim();
		button.setAttribute("aria-label", "Visualizer");
		button.title = "Visualizer";
		button.innerHTML =
			`<span aria-hidden="true" class="e-10860-button__icon-wrapper"><span class="e-10860-icon" ` +
			`style="display:inline-flex;--encore-icon-height:var(--encore-graphic-size-decorative-base);` +
			`--encore-icon-width:var(--encore-graphic-size-decorative-base);` +
			`width:var(--encore-icon-width);height:var(--encore-icon-height)">${ICON}</span></span>`;
		button.addEventListener("click", () => Spicetify.Platform.History.push(ROUTE));
		container.appendChild(button);
		syncActive(button);
	};

	const start = () => {
		if (!Spicetify.Platform?.History) {
			setTimeout(start, 300);
			return;
		}
		ensureButton();
		// React may rebuild the search container; keep the button in place.
		new MutationObserver(ensureButton).observe(document.body, { childList: true, subtree: true });
		Spicetify.Platform.History.listen(() => {
			const button = document.getElementById(ID);
			if (button) syncActive(button);
		});
	};
	start();
})();
