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
    }
};
