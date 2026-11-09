// Kleine hulpfuncties voor Blazor (geen framework).
window.toetsr = {
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            const ok = document.execCommand("copy");
            document.body.removeChild(area);
            return ok;
        }
    },
    scrollToId: function (id, smooth) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: smooth ? "smooth" : "auto", block: "start" });
    },
    scrollTop: function () {
        window.scrollTo({ top: 0 });
    },
    scrollIntoViewIfNeeded: function (selector) {
        const all = document.querySelectorAll(selector);
        const el = all[all.length - 1];
        if (el) el.scrollIntoView({ behavior: "smooth", block: "nearest" });
    },
    // Toetsenbord tijdens een toets: pijltjes/W/S, Enter, Spatie, links/rechts.
    registerExamKeys: function (dotnet) {
        this.unregisterExamKeys();
        const keys = {
            ArrowUp: "up", w: "up", W: "up",
            ArrowDown: "down", s: "down", S: "down",
            Enter: "enter", " ": "toggle",
            ArrowLeft: "prev", ArrowRight: "next"
        };
        this._examKeys = function (e) {
            if (e.ctrlKey || e.altKey || e.metaKey || e.repeat && e.key === "Enter") return;
            const key = keys[e.key];
            if (!key) return;
            if (document.querySelector(".modal-backdrop")) return;
            const t = e.target;
            const tag = t && t.tagName;
            if (tag === "SELECT" || tag === "TEXTAREA" || (tag === "INPUT" && t.type !== "radio" && t.type !== "checkbox")) return;
            if (t && t.isContentEditable) return;
            e.preventDefault();
            // Voorkom dat een knop of optie met focus de toets nog een keer verwerkt.
            if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
            dotnet.invokeMethodAsync("OnExamKey", key);
        };
        document.addEventListener("keydown", this._examKeys);
    },
    unregisterExamKeys: function () {
        if (this._examKeys) document.removeEventListener("keydown", this._examKeys);
        this._examKeys = null;
    }
};

