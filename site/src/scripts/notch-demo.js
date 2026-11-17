// The live notch demo. It follows the real app: the same spring model as Core/Spring.cs,
// the same sizes for each mode, the same popups and pages. It runs on every page; the
// buttons that drive it exist only on the home page, so anything touching them checks
// that they're there.
(() => {
    const $ = (id) => document.getElementById(id);
    const $$ = (s) => [...document.querySelectorAll(s)];
    const bind = (id, fn) => {
        const el = $(id);
        if (el) el.onclick = fn;
    };
    const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const canHover = matchMedia("(hover: hover)").matches;

    const notch = $("notch"),
        island = $("island");
    if (!notch || !island) return;

    /* ---------- Icons. Filled ones are the app's own shapes; the line ones stand in for Segoe Fluent Icons. ---------- */
    const FILL = {
        bolt: ["0 0 13 16", "M9,0 L1,9 H6.5 L5,16 L13,6.5 H7.5 Z"],
        battery: ["0 0 15 12", "M0,2 H13 V4.5 H15 V7.5 H13 V10 H0 Z M1.5,3.5 V8.5 H11.5 V3.5 Z M3,5 H6 V7 H3 Z"],
        check: ["0 0 14 13", "M0,7 L2,5 L5.5,8.5 L12,1.5 L14,3.5 L5.5,12.5 Z"],
        play: ["-1 -1 15 16", "M1,0 L13,7 L1,14 Z"],
        pause: ["-1 -1 15 16", "M0,0 H4 V14 H0 Z M9,0 H13 V14 H9 Z"],
    };
    const LINE = {
        music: "M6 12.2V3.4l7-1.6v8.6M6 12.2a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm7-1.8a2 2 0 1 1-4 0 2 2 0 0 1 4 0Z",
        shelf: "M2.5 5 8 2l5.5 3v6L8 14l-5.5-3ZM2.5 5 8 8l5.5-3M8 8v6",
        bell: "M4 11.5V7a4 4 0 0 1 8 0v4.5l1 1.5H3ZM6.5 14.5a1.6 1.6 0 0 0 3 0",
        timer: "M8 14.5a5.5 5.5 0 1 0 0-11 5.5 5.5 0 0 0 0 11ZM8 6v3M6 1.5h4",
        devices: "M1.5 4h9v7h-9ZM4 13.5h4M12.5 6h2v7.5h-2Z",
        gear: "M8 10.2a2.2 2.2 0 1 0 0-4.4 2.2 2.2 0 0 0 0 4.4ZM8 1.5v1.6M8 12.9v1.6M1.5 8h1.6M12.9 8h1.6M3.4 3.4l1.1 1.1M11.5 11.5l1.1 1.1M3.4 12.6l1.1-1.1M11.5 4.5l1.1-1.1",
        wifi: "M1.5 6a9.5 9.5 0 0 1 13 0M3.7 8.4a6.3 6.3 0 0 1 8.6 0M5.9 10.8a3.1 3.1 0 0 1 4.2 0M8 13.2h.01",
        bt: "M4.5 5l7 6L8 14V2l3.5 3-7 6",
        usb: "M5 6.5h6v7.5H5ZM6.5 6.5V2h3v4.5",
        vol: "M2 6h2.5L8 3v10L4.5 10H2ZM10.5 5.5a3.5 3.5 0 0 1 0 5M12.5 3.5a6.3 6.3 0 0 1 0 9",
        volLow: "M2 6h2.5L8 3v10L4.5 10H2ZM10.5 5.5a3.5 3.5 0 0 1 0 5",
        mute: "M2 6h2.5L8 3v10L4.5 10H2ZM10.5 6l4 4M14.5 6l-4 4",
    };
    const fill = (n) => `<svg viewBox="${FILL[n][0]}" aria-hidden="true"><path fill-rule="evenodd" d="${FILL[n][1]}"/></svg>`;
    const line = (n) => `<svg class="line" viewBox="0 0 16 16" aria-hidden="true"><path d="${LINE[n]}"/></svg>`;

    /* ---------- Data. Songs, artists and lyrics are made up for the demo. ---------- */
    const SONGS = [
        {
            title: "Slow Monsoon",
            artist: "Kite Season",
            dur: 204,
            a: "#ff8a3d",
            b: "#8a2be2",
            lines: [
                "Rain on the tin roof, counting the time",
                "Every light in the valley is mine",
                "Hold the kite string, let the wind decide",
                "We were never in a hurry to arrive",
                "Slow monsoon, carry me home",
                "Down the old road that the rivers know",
                "Slow monsoon, take it slow",
                "I will be here when the clouds let go",
            ],
        },
        {
            title: "Glass Garden",
            artist: "Nova Lane",
            dur: 188,
            a: "#2bd1c4",
            b: "#1f4bd8",
            lines: [
                "Morning hits the window like a bell",
                "Green things growing where the city fell",
                "I kept a garden made of glass",
                "Careful steps so nothing cracks",
                "Let the light through, let the light through",
                "Every colour that I owe to you",
            ],
        },
    ];
    const NOTIFS = [
        { app: "Mail", c: "#0a84ff", l: "M", title: "Notch 0.3.0 is out", body: "File shelf, sleep and a charging indicator" },
        { app: "Messages", c: "#30d158", l: "S", title: "Sita", body: "Sent you a photo from the trek" },
        { app: "Calendar", c: "#ff453a", l: "14", title: "Design review at 3:00", body: "Starts in 10 minutes" },
    ];
    const ACCENTS = ["#30D158", "#0A84FF", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFFFFF"];
    const PAGES = ["music", "shelf", "notifications", "timer", "devices"];
    const PAGE_ICON = { music: "music", shelf: "shelf", notifications: "bell", timer: "timer", devices: "devices" };
    const SPEED = { Relaxed: 1.35, Normal: 1, Fast: 0.7 };

    // Sizes from the app (NotchWindow.xaml.cs).
    const VIEW_W = { idle: 168, media: 168, lyric: 340, banner: 380, volume: 380, notif: 380 };
    const DOTS_ROOM = 14,
        TABS_ROOM = 26,
        SLEEP_W = 96,
        SLEEP_H = 6;

    const S = {
        style: "Floating",
        open: canHover ? "Hover" : "Click",
        speed: "Normal",
        accent: ACCENTS[0],
        sleep: 10, // seconds untouched before the notch tucks away; 0 never
    };
    const st = {
        mode: "closed", // sleep | closed | hud | open
        page: "music",
        hud: "banner",
        tabs: false, // page icons showing instead of the dots
        hasMedia: false, // a player has a session (nothing yet: the music page shows a clock)
        song: 0,
        t: 0,
        playing: false,
        lyrics: false,
        muted: false,
        vol: 48,
        notifs: [
            { app: "Messages", c: "#30d158", l: "A", title: "Aarav", body: "Are we still on for momo at 7?", time: "2m" },
            { app: "Calendar", c: "#ff453a", l: "14", title: "Standup in 5 minutes", body: "Room 2 · 6 people", time: "8m" },
        ],
        nIdx: 0,
        timerLen: 1500,
        timerLeft: 1500,
        timerRun: false,
        wifi: true,
        bt: true,
        usb: true,
        batt: 0.82,
        charging: false,
        files: [],
        dragIn: false,
        dragOut: false,
        hudFromSleep: false,
    };

    /* ---------- Spring: same model as Core/Spring.cs; keeps its velocity when the target moves ---------- */
    class Spring {
        constructor(v) {
            this.v = this.t = v;
            this.vel = 0;
            this.k = 100;
            this.c = 20;
        }
        to(t, resp, z) {
            this.t = t;
            const f = (2 * Math.PI) / Math.max(0.05, resp);
            this.k = f * f;
            this.c = 2 * z * f;
            if (reduce) this.snap(t);
        }
        snap(v) {
            this.v = this.t = v;
            this.vel = 0;
        }
        step(dt) {
            if (this.v === this.t && this.vel === 0) return false;
            while (dt > 0) {
                const h = Math.min(dt, 0.004);
                this.vel += (this.k * (this.t - this.v) - this.c * this.vel) * h;
                this.v += this.vel * h;
                dt -= h;
            }
            if (Math.abs(this.t - this.v) < 0.01 && Math.abs(this.vel) < 0.05) this.snap(this.t);
            return true;
        }
    }
    const W = new Spring(168),
        H = new Spring(32),
        R = new Spring(16),
        SC = new Spring(1);
    const springs = [W, H, R, SC];

    const views = $$(".v");
    const view = (name) => views.find((v) => v.dataset.v === name);
    views.forEach((v) => {
        if (VIEW_W[v.dataset.v]) v.style.setProperty("--w", VIEW_W[v.dataset.v] + "px");
    });

    let raf = 0,
        last = 0;
    function kick() {
        if (!raf) {
            last = performance.now();
            raf = requestAnimationFrame(frame);
        }
    }
    function frame(now) {
        const dt = Math.min(0.05, (now - last) / 1000);
        last = now;
        let moving = false;
        for (const s of springs) moving = s.step(dt) || moving;
        draw();
        raf = moving ? requestAnimationFrame(frame) : 0;
    }
    function draw() {
        island.style.width = W.v + "px";
        island.style.height = Math.max(0, H.v) + "px";
        const r = Math.max(0, Math.min(R.v, H.v / 2));
        const joined = S.style === "Classic" || st.mode === "sleep";
        island.style.borderRadius = joined ? `0 0 ${r}px ${r}px` : r + "px";
        $("notchIn").style.scale = SC.v;
        // The dots sit just above the bottom edge; the icon row 8px above it.
        $("dots").style.setProperty("--y", H.v - 16 + "px");
        $("tabs").style.setProperty("--y", H.v - 38 + "px");
    }

    /* ---------- Modes ---------- */
    function closedView() {
        if (st.timerRun) return "idle";
        if (st.hasMedia) return st.lyrics ? "lyric" : "media";
        return "idle";
    }
    function target() {
        const classic = S.style === "Classic";
        if (st.mode === "sleep") return { view: "", w: SLEEP_W, h: SLEEP_H, r: SLEEP_H / 2 };
        if (st.mode === "open") {
            const v = view(st.page);
            const h = v.offsetHeight + DOTS_ROOM + (st.tabs ? TABS_ROOM : 0);
            return { view: st.page, w: v.offsetWidth, h, r: classic ? 24 : 30 };
        }
        if (st.mode === "hud") {
            const h = view(st.hud).offsetHeight;
            return { view: st.hud, w: 380, h, r: classic ? 22 : Math.min(h / 2, 30) };
        }
        const pill = closedView();
        return { view: pill, w: VIEW_W[pill], h: 32, r: classic ? 10 : 16 };
    }

    let shown = { mode: "closed", view: "idle" },
        area = 168 * 32;
    function apply() {
        island.classList.toggle("has-media", st.hasMedia);
        island.classList.toggle("lyr", st.hasMedia && st.lyrics);
        island.classList.toggle("playing", st.playing);
        island.classList.toggle("open", st.mode === "open");
        island.classList.toggle("tabs-open", st.mode === "open" && st.tabs);
        notch.classList.toggle("tucked", st.mode === "sleep");

        const t = target();
        const before = shown;
        shown = { mode: st.mode, view: t.view };
        views.forEach((v) => {
            const on = v.dataset.v === t.view;
            v.classList.toggle("on", on);
            v.inert = !on;
        });

        // Growing gets a soft overshoot; shrinking settles without one (IslandShape.MoveTo).
        const k = SPEED[S.speed];
        const growing = t.w * t.h >= area;
        area = t.w * t.h;
        const resp = (growing ? 0.5 : 0.42) * k,
            z = growing ? 0.78 : 0.95;
        W.to(t.w, resp, z);
        H.to(t.h, resp, z);
        R.to(t.r, resp, 1);
        SC.to(Math.min(1, (innerWidth - 24) / t.w), resp, 1);

        // Album colour: on the music page, or the closed notch with a lyric line.
        const song = SONGS[st.song];
        const glow = st.hasMedia && ((st.mode === "open" && st.page === "music") || t.view === "lyric");
        island.style.setProperty("--glow", rgb(song.a));
        island.classList.toggle("glow", glow);

        renderDots();
        renderHint();
        kick();

        // The clock, cover and timer fly between the pill and the page.
        if (before.mode === "closed" && st.mode === "open") morph(pairsFor(before.view, st.page), 620 * k);
        else if (before.mode === "open" && st.mode === "closed")
            morph(pairsFor(t.view, before.view).map(([a, b]) => [b, a]), 420 * k);
        else if (before.mode !== st.mode) endMorph();

        armSleep();
    }
    const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)).join(" ");

    /* ---------- Morph: copies fly between the pill and the page; both ends are measured
       on every frame, since the island is still resizing ---------- */
    let morphRaf = 0,
        morphs = [];
    const visible = (el) => el && el.offsetParent !== null;
    function pairsFor(closedName, page) {
        const pill = view(closedName),
            pv = view(page);
        if (!pill || !pv) return [];
        const src = (key) => pill.querySelector(`[data-morph="${key}"]`);
        const dst = (key) => [...pv.querySelectorAll(`[data-target="${key}"]`)].find(visible);
        const pairs = st.timerRun
            ? [
                  [src("clock"), dst("countdown")],
                  [src("ring"), dst("ring")],
              ]
            : [
                  [src("clock"), dst("clock")],
                  [src("art"), page === "music" && st.hasMedia ? $("mArt") : null],
              ];
        return pairs.filter(([a, b]) => visible(a) && visible(b));
    }
    function ghostOf(el) {
        const g = el.cloneNode(true);
        g.removeAttribute("id");
        g.querySelectorAll("[id]").forEach((n) => n.removeAttribute("id"));
        const cs = getComputedStyle(el);
        for (const p of [
            "fontFamily",
            "fontSize",
            "fontWeight",
            "lineHeight",
            "color",
            "letterSpacing",
            "fontVariantNumeric",
            "borderRadius",
            "backgroundImage",
            "backgroundColor",
            "opacity",
        ])
            g.style[p] = cs[p];
        g.style.width = el.offsetWidth + "px";
        g.style.height = el.offsetHeight + "px";
        g.classList.add("ghost");
        g.setAttribute("aria-hidden", "true");
        island.append(g);
        return g;
    }
    function endMorph() {
        cancelAnimationFrame(morphRaf);
        morphRaf = 0;
        for (const m of morphs) {
            m.a.remove();
            m.b.remove();
            m.src.style.visibility = m.dst.style.visibility = "";
        }
        morphs = [];
    }
    function morph(pairs, ms) {
        endMorph();
        if (reduce || !pairs.length) return;

        morphs = pairs.map(([src, dst]) => {
            const m = { src, dst, a: ghostOf(src), b: ghostOf(dst) };
            m.aOp = +getComputedStyle(src).opacity || 1;
            m.bOp = +getComputedStyle(dst).opacity || 1;
            return m;
        });
        for (const m of morphs) {
            m.src.style.visibility = m.dst.style.visibility = "hidden";
            m.b.style.opacity = "0";
        }

        const start = performance.now();
        const centre = (el, box, sc) => {
            const r = el.getBoundingClientRect();
            return { x: (r.left + r.width / 2 - box.left) / sc, y: (r.top + r.height / 2 - box.top) / sc, h: r.height / sc };
        };
        const place = (g, x, y, h) => {
            const s = h / g.offsetHeight;
            g.style.transform = `translate(${x - (g.offsetWidth * s) / 2}px, ${y - h / 2}px) scale(${s})`;
        };
        const tick = (now) => {
            const t = Math.min(1, (now - start) / ms);
            const p = 1 - Math.exp(-7 * t) * Math.cos(5 * t); // springy, with a touch of overshoot
            const box = island.getBoundingClientRect(),
                sc = SC.v || 1;
            for (const m of morphs) {
                const a = centre(m.src, box, sc),
                    b = centre(m.dst, box, sc);
                const x = a.x + (b.x - a.x) * p,
                    y = a.y + (b.y - a.y) * p,
                    h = a.h + (b.h - a.h) * p;
                place(m.a, x, y, h);
                place(m.b, x, y, h);
                m.a.style.opacity = String(m.aOp * Math.max(0, 1 - t / 0.4));
                m.b.style.opacity = String(m.bOp * Math.min(1, t / 0.5));
            }
            if (t < 1) morphRaf = requestAnimationFrame(tick);
            else endMorph();
        };
        morphRaf = requestAnimationFrame(tick);
    }

    /* ---------- Opening, closing, popups ---------- */
    let closeT = 0,
        hudT = 0,
        hovering = false,
        pointerType = "mouse",
        hudPage = "music";
    function openPage(p) {
        clearTimeout(hudT);
        clearTimeout(closeT);
        if (p && p !== st.page) {
            st.page = p;
            st.tabs = st.tabs && st.mode === "open";
        }
        st.mode = "open";
        apply();
        if (!hovering) closeT = setTimeout(close, 6000);
    }
    function close() {
        clearTimeout(closeT);
        st.mode = "closed";
        st.tabs = false;
        apply();
    }
    function showHud(kind, ms = 2600) {
        if (st.mode === "open" && hovering) return;
        clearTimeout(closeT);
        if (st.mode !== "hud") st.hudFromSleep = st.mode === "sleep";
        st.hud = kind;
        st.mode = "hud";
        st.tabs = false;
        apply();
        clearTimeout(hudT);
        hudT = setTimeout(() => {
            if (!hovering && st.mode === "hud") endHud();
        }, ms);
    }
    // A popup that woke the notch puts it back to sleep.
    function endHud() {
        if (st.hudFromSleep && S.sleep) sleep();
        else close();
    }

    island.addEventListener("pointerdown", (e) => {
        pointerType = e.pointerType;
    });
    island.addEventListener("pointerenter", (e) => {
        if (e.pointerType !== "mouse") return;
        hovering = true;
        clearTimeout(closeT);
        clearTimeout(sleepT);
        if (S.open === "Hover" && st.mode !== "open") openPage(st.mode === "hud" ? hudPage : null);
        else if (st.mode === "sleep") wake();
    });
    island.addEventListener("pointerleave", (e) => {
        if (e.pointerType !== "mouse") return;
        hovering = false;
        if (st.mode === "open" && !st.dragOut) closeT = setTimeout(close, 260);
        if (st.mode === "hud") {
            clearTimeout(hudT);
            hudT = setTimeout(endHud, 1200);
        }
        armSleep();
    });
    island.addEventListener("click", (e) => {
        if (e.target.closest("button, .file")) return;
        if (st.mode === "open" && e.target.closest(".card")) return;
        if (st.mode !== "open") openPage(st.mode === "hud" ? hudPage : null);
        else if (S.open === "Click" || pointerType !== "mouse") close();
    });
    document.addEventListener("pointerdown", (e) => {
        if (st.mode === "open" && !e.target.closest(".notch, [data-keep]")) close();
    });

    // Scroll or swipe sideways to switch pages.
    let wheelLock = 0;
    island.addEventListener(
        "wheel",
        (e) => {
            if (st.mode !== "open") return;
            const row = e.target.closest(".sh-row");
            if (row && row.scrollWidth > row.clientWidth && Math.abs(e.deltaX) >= Math.abs(e.deltaY)) return;
            e.preventDefault();
            const d = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
            if (Math.abs(d) < 8 || performance.now() < wheelLock) return;
            wheelLock = performance.now() + 420;
            const i = PAGES.indexOf(st.page);
            openPage(PAGES[(i + (d > 0 ? 1 : -1) + PAGES.length) % PAGES.length]);
        },
        { passive: false },
    );

    /* ---------- Page dots, and the page icons that replace them when the pointer rests there ---------- */
    function renderDots() {
        const d = $("dots");
        if (!d.children.length)
            PAGES.forEach((p) => {
                const b = document.createElement("button");
                b.setAttribute("aria-label", p);
                b.innerHTML = "<i></i>";
                b.onclick = () => openPage(p);
                d.append(b);
            });
        [...d.children].forEach((b, i) => b.setAttribute("aria-pressed", String(PAGES[i] === st.page)));

        const tabs = $("tabs");
        if (!tabs.children.length) {
            PAGES.forEach((p) => {
                const b = document.createElement("button");
                b.setAttribute("aria-label", p);
                b.innerHTML = line(PAGE_ICON[p]);
                b.onclick = () => openPage(p);
                tabs.append(b);
            });
            tabs.insertAdjacentHTML("beforeend", '<span class="sep"></span>');
            const gear = document.createElement("button");
            gear.setAttribute("aria-label", "Settings");
            gear.innerHTML = line("gear");
            gear.onclick = () => document.querySelector(".settings")?.scrollIntoView({ behavior: "smooth", block: "center" });
            tabs.append(gear);
        }
        [...tabs.querySelectorAll("button")].forEach((b, i) =>
            b.setAttribute("aria-pressed", String(PAGES[i] === st.page)),
        );
    }
    // The icons open only once the pointer rests on the dots (380ms, drifting under 10px).
    let tabsOpenT = 0,
        tabsCloseT = 0,
        restX = 0,
        restY = 0;
    function setTabs(on) {
        if (st.tabs === on || st.mode !== "open") return;
        st.tabs = on;
        apply();
    }
    $("dots").addEventListener("pointerenter", (e) => {
        clearTimeout(tabsCloseT);
        restX = e.clientX;
        restY = e.clientY;
        const check = () => {
            if (!$("dots").matches(":hover")) return;
            if (Math.hypot(lastX - restX, lastY - restY) > 10) {
                restX = lastX;
                restY = lastY;
                tabsOpenT = setTimeout(check, 380);
                return;
            }
            setTabs(true);
        };
        tabsOpenT = setTimeout(check, 380);
    });
    let lastX = 0,
        lastY = 0;
    $("dots").addEventListener("pointermove", (e) => {
        lastX = e.clientX;
        lastY = e.clientY;
    });
    $("dots").addEventListener("pointerleave", () => {
        clearTimeout(tabsOpenT);
        tabsCloseT = setTimeout(() => setTabs(false), 320);
    });
    $("tabs").addEventListener("pointerenter", () => clearTimeout(tabsCloseT));
    $("tabs").addEventListener("pointerleave", () => {
        tabsCloseT = setTimeout(() => setTabs(false), 320);
    });

    /* ---------- Sleep: after a while untouched, tuck into a strip against the top edge ---------- */
    let sleepT = 0;
    function armSleep() {
        clearTimeout(sleepT);
        if (!S.sleep || st.mode !== "closed" || hovering || st.dragIn) return;
        sleepT = setTimeout(sleep, S.sleep * 1000);
    }
    function sleep() {
        if (hovering || st.dragIn || (st.mode !== "closed" && st.mode !== "hud")) return;
        st.mode = "sleep";
        apply();
    }
    function wake() {
        if (st.mode !== "sleep") return;
        st.mode = "closed";
        apply();
    }
    function renderHint() {
        const h = $("hint");
        if (!h) return;
        h.textContent =
            st.mode === "sleep"
                ? "↑ Asleep. Point at the strip at the top"
                : canHover
                  ? "↑ Hover the notch at the top"
                  : "↑ Tap the notch at the top";
    }

    /* ---------- Rendering ---------- */
    const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;
    const pct = (n) => Math.round(n * 100) + "%";
    const setRing = (el, value) => el && (el.style.strokeDashoffset = String(100 * (1 - Math.max(0, Math.min(1, value)))));
    const esc = (t) =>
        String(t).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

    function lyricIndex() {
        const n = SONGS[st.song].lines.length;
        return st.t < 4 ? -1 : Math.floor((st.t - 4) / 4.2) % n;
    }
    let lastLine = -2;
    function renderSong(full) {
        const s = SONGS[st.song];
        if (full) {
            $$(".cover").forEach((c) => {
                c.style.setProperty("--ca", s.a);
                c.style.setProperty("--cb", s.b);
            });
            $("mTitle").textContent = s.title;
            $("mArtist").textContent = s.artist;
            $("lines").innerHTML = s.lines.map((l) => `<p>${esc(l)}</p>`).join("");
            lastLine = -2;
        }
        $("mCur").textContent = fmt(st.t);
        $("mLeft").textContent = "-" + fmt(s.dur - st.t);
        $("mBar").style.width = (st.t / s.dur) * 100 + "%";
        const icon = FILL[st.playing ? "pause" : "play"];
        $("playIco").innerHTML = `<path d="${icon[1]}"/>`;
        $("playIco").setAttribute("width", st.playing ? "12" : "13");
        $("playBtn").setAttribute("aria-label", st.playing ? "Pause" : "Play");
        if ($("dPlay")) $("dPlay").textContent = st.playing ? "Pause" : "Play";

        const i = lyricIndex();
        if (i !== lastLine) {
            lastLine = i;
            const ps = [...$("lines").children];
            ps.forEach((p, k) => p.classList.toggle("on", k === i));
            const cur = ps[Math.max(0, i)];
            if (cur) $("lines").style.translate = `0 ${64 - cur.offsetTop - cur.offsetHeight / 2}px`;
            // Before the first line, the pill shows the song's title.
            $("pillLine").textContent = i < 0 ? s.title : s.lines[i];
        }
    }
    function renderControls() {
        $("lyrBtn").setAttribute("aria-pressed", String(st.lyrics));
        $("dLyrics")?.setAttribute("aria-pressed", String(st.lyrics));
        $("muteBtn").innerHTML = line(st.muted ? "mute" : st.vol < 34 ? "volLow" : "vol");
        $("muteBtn").setAttribute("aria-pressed", String(st.muted));
    }
    const cardHtml = (n) =>
        `<div class="card"><span class="app-ico" style="--c:${n.c}">${esc(n.l)}</span><span class="card-text"><small><span>${esc(n.app)}</span><span>${n.time || "now"}</span></small><b>${esc(n.title)}</b><p>${esc(n.body)}</p></span></div>`;
    function renderNotifs() {
        const list = st.notifs.slice(0, 3);
        $("nHead").textContent = list.length ? `Notifications  ·  ${st.notifs.length}` : "Notifications";
        $("clearBtn").hidden = !list.length;
        $("nlist").innerHTML = list.length ? list.map(cardHtml).join("") : '<div class="empty">No notifications</div>';
    }
    function renderTimer() {
        $("tLeft").textContent = fmt(st.timerLeft);
        setRing($("tRing"), st.timerLeft / st.timerLen);
        const paused = !st.timerRun && st.timerLeft < st.timerLen;
        $("tGo").textContent = st.timerRun ? "Pause" : paused ? "Resume" : "Start";
        $("tState").textContent = st.timerRun ? "Focusing" : paused ? "Paused" : "Focus timer";
        [...$("presets").children].forEach((b) => b.classList.toggle("on", +b.dataset.m * 60 === st.timerLen));
        renderPill();
    }
    function renderDevices() {
        $("wifiTile").className = "tile" + (st.wifi ? " on" : "");
        $("wifiTile").innerHTML = `<span class="dot">${line("wifi")}</span><span class="tile-text"><b>Wi-Fi</b><span>${st.wifi ? "Home 5G" : "Off"}</span></span><span class="chev">›</span>`;
        $("btTile").className = "tile" + (st.bt ? " on" : "");
        $("btTile").innerHTML = `<span class="dot">${line("bt")}</span><span class="tile-text"><b>Bluetooth</b><span>${st.bt ? "Galaxy Buds2" : "Off"}</span></span><span class="chev">›</span>`;
        const rows = [];
        if (st.usb)
            rows.push(
                `<div class="dev"><span class="dev-ico">${line("usb")}</span><span class="dev-text"><b>Kingston 64GB</b><span>USB drive</span></span><button class="pill-btn alt" data-act="open">Open</button><button class="pill-btn" data-act="eject">Eject</button></div>`,
            );
        if (st.bt)
            rows.push(
                `<div class="dev"><span class="dev-ico">${line("bt")}</span><span class="dev-text"><b>Galaxy Buds2</b><span>Bluetooth</span></span><button class="pill-btn alt" data-act="bt">Open</button></div>`,
            );
        $("drow").innerHTML = rows.join("") || '<div class="dev-empty">No drives, phones or Bluetooth devices connected</div>';
    }
    const timeText = (d) => d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }).replace(/\s?[ap]\.?m\.?$/i, "");
    function renderClock() {
        const now = new Date();
        const time = timeText(now);
        $("mTime").textContent = time;
        $("iTime").textContent = time;
        $("pClockMedia").textContent = time;
        $("mDate").textContent = now.toLocaleDateString([], { weekday: "short", day: "numeric", month: "short" });
        $("iDate").textContent = now.toLocaleDateString([], { weekday: "long", day: "numeric", month: "long" });
        renderPill();
    }
    // The closed pill: a running timer wins; otherwise the battery and the time.
    function renderPill() {
        const ring = $("pRing").closest(".ring");
        ring.classList.toggle("timer", st.timerRun);
        if (st.timerRun) {
            $("pClock").textContent = fmt(st.timerLeft);
            setRing($("pRing"), st.timerLeft / st.timerLen);
            ring.classList.remove("warn");
        } else {
            $("pClock").textContent = timeText(new Date());
            setRing($("pRing"), st.batt);
            ring.classList.toggle("warn", !st.charging && st.batt <= 0.2);
        }
    }
    function renderBattery() {
        $$(".b-fill").forEach((r) => r.setAttribute("width", String(Math.max(2.5, 17 * st.batt))));
        $$(".b-num").forEach((n) => (n.textContent = pct(st.batt)));
        $("iBatt").textContent = pct(st.batt) + (st.charging ? " · Charging" : "");
        island.classList.toggle("charging", st.charging);
        island.classList.toggle("low", !st.charging && st.batt <= 0.2);
        renderPill();
    }

    /* ---------- Popups, worded as in the app ---------- */
    function banner(icon, ring, warn, title, sub, value = "") {
        $("bnIco").innerHTML = FILL[icon] ? fill(icon) : line(icon);
        setRing($("bnRing"), ring);
        $("bnRing").closest(".ring").classList.toggle("warn", warn);
        $("bnTitle").textContent = title;
        $("bnSub").textContent = sub;
        $("bnValue").textContent = value;
        showHud("banner");
    }
    const HUDS = {
        notif() {
            const n = NOTIFS[st.nIdx++ % NOTIFS.length];
            st.notifs = [n, ...st.notifs.filter((x) => x !== n)].slice(0, 6);
            $("ntCard").outerHTML = cardHtml(n).replace('<div class="card">', '<div class="card" id="ntCard">');
            renderNotifs();
            hudPage = "notifications";
            showHud("notif", 5000);
        },
        charger() {
            st.charging = !st.charging;
            renderBattery();
            hudPage = "music";
            const title = !st.charging ? "On battery" : st.batt >= 1 ? "Fully charged" : "Charging";
            banner(
                st.charging ? "bolt" : "battery",
                st.batt,
                false,
                title,
                st.charging ? "Charger connected" : "Charger disconnected",
                pct(st.batt),
            );
        },
        battery() {
            hudPage = "music";
            banner("battery", 0.2, true, "Low battery", "Plug in your charger", "20%");
        },
        volume() {
            st.vol = st.vol >= 88 ? 16 : st.vol + 12;
            st.muted = false;
            $("volIco").innerHTML = line(st.vol < 34 ? "volLow" : "vol");
            $("volBar").style.setProperty("--vol", st.vol + "%");
            renderControls();
            hudPage = "music";
            showHud("volume", 1600);
        },
        usb() {
            st.usb = true;
            renderDevices();
            hudPage = "devices";
            banner("usb", 1, false, "Kingston 64GB", "USB drive connected · click to open");
        },
        focus() {
            hudPage = "timer";
            banner("check", 1, false, "Time's up", `${Math.round(st.timerLen / 60)} minute focus session finished`);
        },
    };

    /* ---------- Music ---------- */
    let songTick = 0;
    function setPlaying(on) {
        if (on && !st.hasMedia) {
            st.hasMedia = true; // a player started: the music page swaps its clock for the player
            renderSong(true);
        }
        st.playing = on;
        clearInterval(songTick);
        if (on)
            songTick = setInterval(() => {
                st.t += 0.25;
                if (st.t >= SONGS[st.song].dur) return nextSong();
                renderSong(false);
            }, 250);
        renderSong(false);
        apply();
    }
    function nextSong(dir = 1) {
        st.song = (st.song + dir + SONGS.length) % SONGS.length;
        st.t = 0;
        renderSong(true);
        renderSong(false);
        apply();
    }
    function toggleLyrics() {
        st.lyrics = !st.lyrics;
        renderControls();
        apply();
        lastLine = -2;
        requestAnimationFrame(() => renderSong(false)); // centre the line once the column has a size
    }
    $("playBtn").onclick = () => setPlaying(!st.playing);
    $("nextBtn").onclick = () => nextSong(1);
    $("prevBtn").onclick = () => (st.t > 3 ? ((st.t = 0), renderSong(false)) : nextSong(-1));
    $("lyrBtn").onclick = toggleLyrics;
    $("muteBtn").onclick = () => {
        st.muted = !st.muted;
        renderControls();
    };
    $("track").onclick = (e) => {
        const r = e.currentTarget.getBoundingClientRect();
        st.t = Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)) * SONGS[st.song].dur;
        renderSong(false);
    };

    /* ---------- Focus timer ---------- */
    let timerTick = 0;
    [15, 25, 45, 60].forEach((m) => {
        const b = document.createElement("button");
        b.className = "chip";
        b.dataset.m = m;
        b.textContent = `${m} min`;
        b.onclick = () => {
            clearInterval(timerTick);
            st.timerRun = false;
            st.timerLen = st.timerLeft = m * 60;
            renderTimer();
        };
        $("presets").append(b);
    });
    $("tGo").onclick = () => {
        st.timerRun = !st.timerRun;
        clearInterval(timerTick);
        if (st.timerRun)
            timerTick = setInterval(() => {
                st.timerLeft = Math.max(0, st.timerLeft - 1);
                if (!st.timerLeft) {
                    clearInterval(timerTick);
                    st.timerRun = false;
                    st.timerLeft = st.timerLen;
                    renderTimer();
                    if (st.mode === "open") close();
                    HUDS.focus();
                    return;
                }
                renderTimer();
            }, 1000);
        renderTimer();
    };
    $("tReset").onclick = () => {
        clearInterval(timerTick);
        st.timerRun = false;
        st.timerLeft = st.timerLen;
        renderTimer();
    };

    /* ---------- Notifications and connections ---------- */
    $("clearBtn").onclick = () => {
        st.notifs = [];
        renderNotifs();
        apply();
    };
    $("wifiTile").onclick = () => {
        st.wifi = !st.wifi;
        renderDevices();
    };
    $("btTile").onclick = () => {
        st.bt = !st.bt;
        renderDevices();
        apply();
    };
    $("drow").onclick = (e) => {
        if (e.target.closest("[data-act]")?.dataset.act === "eject") {
            st.usb = false;
            renderDevices();
            apply();
        }
    };

    /* ---------- Shelf: drop real files from your computer onto the notch ---------- */
    const EXT = {
        pdf: "#ff453a",
        png: "#0a84ff",
        jpg: "#0a84ff",
        jpeg: "#0a84ff",
        gif: "#0a84ff",
        webp: "#0a84ff",
        svg: "#0a84ff",
        zip: "#ff9f0a",
        mp4: "#bf5af2",
        mov: "#bf5af2",
        mp3: "#ff375f",
        wav: "#ff375f",
        docx: "#0a84ff",
        xlsx: "#30d158",
        pptx: "#ff9f0a",
    };
    function addFiles(list) {
        const added = new Set();
        for (const f of list) {
            if (st.files.some((x) => x.name === f.name)) continue;
            const ext = (f.name.includes(".") ? f.name.split(".").pop() : "file").slice(0, 4).toLowerCase();
            const item = { name: f.name, ext, file: f.file || null, url: f.file ? URL.createObjectURL(f.file) : null };
            item.image = !!f.file && f.file.type.startsWith("image/");
            st.files.push(item);
            added.add(item);
        }
        renderShelf(added);
    }
    function renderShelf(added = new Set()) {
        const n = st.files.length;
        $("shCount").textContent = n ? (n === 1 ? "1 item" : `${n} items`) : "";
        view("shelf").classList.toggle("empty", !n);
        $("shClear").hidden = !n;
        $("shRow").innerHTML = st.files
            .map((f, i) => {
                const bg = f.image ? `background-image:url(${f.url});` : "";
                return `<div class="file${added.has(f) ? " pop" : ""}" draggable="true" tabindex="0" data-i="${i}" title="${esc(f.name)}"><span class="thumb" style="--c:${EXT[f.ext] || "#3a3a3c"};${bg}">${f.image ? "" : esc(f.ext)}</span><span class="name">${esc(f.name)}</span><button class="x" aria-label="Remove ${esc(f.name)}">×</button></div>`;
            })
            .join("");
        $("shRow").scrollLeft = $("shRow").scrollWidth; // show the newest
    }
    function setDropHighlight(on) {
        st.dragIn = on;
        view("shelf").classList.toggle("over", on);
    }
    const hasFiles = (e) => [...(e.dataTransfer?.types || [])].includes("Files");

    // Files dragged anywhere over the page wake the notch, so it's there to drop on.
    document.addEventListener("dragenter", (e) => {
        if (hasFiles(e) && !st.dragOut) wake();
    });
    // A file dropped beside the notch shouldn't open in the browser.
    document.addEventListener("dragover", (e) => {
        if (hasFiles(e) && !island.contains(e.target)) {
            e.preventDefault();
            e.dataTransfer.dropEffect = "none";
        }
    });
    document.addEventListener("drop", (e) => {
        if (hasFiles(e) && !island.contains(e.target)) e.preventDefault();
    });
    island.addEventListener("dragover", (e) => {
        if (st.dragOut || !hasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "copy";
        if (!st.dragIn) {
            setDropHighlight(true);
            openPage("shelf");
            clearTimeout(closeT);
        }
    });
    island.addEventListener("dragleave", (e) => {
        if (island.contains(e.relatedTarget)) return;
        if (st.dragIn) {
            setDropHighlight(false);
            closeT = setTimeout(close, 400);
        } else if (st.dragOut && st.mode === "open") {
            close(); // out of the way, so the page beneath can take it
        }
    });
    island.addEventListener("drop", (e) => {
        if (st.dragOut || !hasFiles(e)) return;
        e.preventDefault();
        setDropHighlight(false);
        addFiles([...e.dataTransfer.files].map((file) => ({ name: file.name, file })));
        clearTimeout(closeT);
        closeT = setTimeout(close, 6000);
    });
    $("shRow").addEventListener("click", (e) => {
        const tile = e.target.closest(".file");
        if (!tile) return;
        const i = +tile.dataset.i,
            f = st.files[i];
        if (e.target.closest(".x")) {
            if (f.url) URL.revokeObjectURL(f.url);
            st.files.splice(i, 1);
            renderShelf();
            return;
        }
        if (f.url) window.open(f.url, "_blank", "noopener");
    });
    $("shRow").addEventListener("dragstart", (e) => {
        const tile = e.target.closest(".file");
        if (!tile) return;
        const f = st.files[+tile.dataset.i];
        st.dragOut = true;
        e.dataTransfer.effectAllowed = "copyMove";
        e.dataTransfer.setData("text/plain", f.name);
        // Chrome and Edge can drop a real file on the desktop from this.
        if (f.file) e.dataTransfer.setData("DownloadURL", `${f.file.type || "application/octet-stream"}:${f.name}:${f.url}`);
    });
    $("shRow").addEventListener("dragend", () => {
        st.dragOut = false;
        if (!hovering && st.mode === "open") close();
    });
    $("shClear").onclick = () => {
        st.files.forEach((f) => f.url && URL.revokeObjectURL(f.url));
        st.files = [];
        renderShelf();
    };

    /* ---------- The home page's controls ---------- */
    function seg(el, options, value, onChange) {
        if (!el) return;
        const ind = document.createElement("span");
        ind.className = "ind";
        const wrap = document.createElement("div");
        wrap.className = "seg";
        wrap.setAttribute("role", "group");
        wrap.append(ind);
        const btns = options.map((o) => {
            const b = document.createElement("button");
            b.type = "button";
            b.textContent = o;
            b.setAttribute("aria-pressed", String(o === value));
            b.onclick = () => {
                btns.forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
                place();
                onChange(o);
            };
            wrap.append(b);
            return b;
        });
        const place = () => {
            const on = btns.find((b) => b.getAttribute("aria-pressed") === "true");
            if (on && on.offsetWidth) wrap.style.cssText = `--x:${on.offsetLeft}px;--w:${on.offsetWidth}px`;
        };
        el.replaceWith(wrap);
        requestAnimationFrame(() => {
            place();
            requestAnimationFrame(() => wrap.classList.add("ready"));
        });
        addEventListener("resize", place);
        document.fonts?.ready.then(place);
    }
    seg($("segStyle"), ["Floating", "Classic"], S.style, (v) => {
        S.style = v;
        notch.classList.toggle("classic", v === "Classic");
        apply();
    });
    seg($("segOpen"), ["Hover", "Click"], S.open, (v) => {
        S.open = v;
    });
    seg($("segSpeed"), ["Relaxed", "Normal", "Fast"], S.speed, (v) => {
        S.speed = v;
    });
    seg($("segSleep"), ["Off", "5 s", "10 s"], "10 s", (v) => {
        S.sleep = parseInt(v) || 0;
        if (!S.sleep) wake();
        armSleep();
    });
    ACCENTS.forEach((c) => {
        const sw = $("swatches");
        if (!sw) return;
        const b = document.createElement("button");
        b.className = "swatch";
        b.style.setProperty("--c", c);
        b.setAttribute("aria-label", "Accent " + c);
        b.setAttribute("aria-pressed", String(c === S.accent));
        b.onclick = () => {
            S.accent = c;
            document.documentElement.style.setProperty("--accent", c);
            $$(".swatch").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
        };
        sw.append(b);
    });

    bind("dPlay", () => setPlaying(!st.playing));
    bind("dNext", () => {
        if (!st.playing) setPlaying(true);
        nextSong(1);
    });
    bind("dLyrics", () => {
        if (!st.hasMedia) setPlaying(true);
        toggleLyrics();
    });
    bind("dShelf", () => openPage("shelf"));
    bind("dSamples", () => {
        addFiles([{ name: "Trek photos.zip" }, { name: "Invoice 0317.pdf" }, { name: "Screen recording.mp4" }, { name: "Lyrics notes.txt" }]);
        openPage("shelf");
    });
    bind("dSleep", () => {
        if (st.mode !== "closed") close();
        sleep();
    });
    $$("[data-page]").forEach((b) => (b.onclick = () => openPage(b.dataset.page)));
    $$("[data-hud]").forEach((b) => (b.onclick = () => HUDS[b.dataset.hud]()));
    bind("copyBtn", async () => {
        const text = $("cmd").textContent.trim();
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            const ta = Object.assign(document.createElement("textarea"), { value: text });
            document.body.append(ta);
            ta.select();
            document.execCommand("copy");
            ta.remove();
        }
        $("copyBtn").textContent = "Copied";
        setTimeout(() => ($("copyBtn").textContent = "Copy"), 1600);
    });
    addEventListener("resize", apply);

    /* ---------- Battery, where the browser shares it ---------- */
    navigator
        .getBattery?.()
        .then((b) => {
            const sync = () => {
                st.batt = b.level;
                st.charging = b.charging;
                renderBattery();
            };
            sync();
            b.addEventListener("levelchange", sync);
            b.addEventListener("chargingchange", sync);
        })
        .catch(() => {});

    /* ---------- First paint ---------- */
    renderSong(true);
    renderSong(false);
    renderControls();
    renderNotifs();
    renderTimer();
    renderDevices();
    renderClock();
    renderBattery();
    renderShelf();
    setInterval(renderClock, 15000);
    $("volIco").innerHTML = line("vol");
    $("volBar").style.setProperty("--vol", st.vol + "%");
    apply();
    springs.forEach((s) => s.snap(s.t));
    draw();
})();
