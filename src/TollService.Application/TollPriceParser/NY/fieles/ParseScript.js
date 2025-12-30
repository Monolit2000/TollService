(async function scrapeNYThruway() {
    const vehicleClass = 6; // меняй класс тут, если нужно
    const baseUrl = "https://tollcalculator.thruway.ny.gov/index.aspx";
    const parser = new DOMParser();

    // ---- FULL LIST ENTRY / EXIT ----
    const points = [
        "m00x", "m01x", "m02x", "m03x", "m04x", "m05x", "m06x", "m06a", "m07x", "m07a", "m08x", "m08a", "m09x", "m10x", "m11x", "m12x", "m13x", "m14x", "m14a", "m14b", "m15x", "m15a", "m16x", "m17x", "m18x", "m19x",
        "m20x", "m21x", "m21b", "b1x", "b2x", "b3x", "bxx", "m22x", "m23x", "m24x", "m25x", "m25a", "m26x", "m27x", "m28x", "m29x", "m29a", "m30x", "m31x", "m32x", "m33x", "m34x", "m34a", "m35x", "m36x", "m37x",
        "m38x", "m39x", "m40x", "m41x", "m42x", "m43x", "m44x", "m45x", "m46x", "m47x", "m48x", "m48a", "m49x", "m50x", "m50a", "m51x", "m52x", "m52a", "m53x", "m54x", "m55x", "m56x", "m57x", "m57a", "m58x",
        "m59x", "m60x", "m61x", "mpax", "ns01x", "ns02x", "ns03x", "ns04x", "ns05x", "ns06x", "ns07x", "ns08x", "ns09x", "ns11x", "ns12x", "ns13x",
        "ns14x", "ns15x", "ns16x", "ns17x", "ns18a", "ns18b", "ns18x", "ns19x", "ns20x", "ns20a", "ns20b", "ns21x", "nsnex", "ne00x", "ne08x", "ne09x", "ne10x", "ne11x", "ne12x", "ne13x", "ne14x",
        "ne15x", "ne16x", "ne17x", "ne18a", "ne18b", "ne19x", "ne20x", "ne21x", "ne22x", "nectx", "sub"
    ];

    // ---- GENERATE ALL ROUTES ----
    const routes = [];
    for (const e of points) for (const x of points) if (e !== x) routes.push({ entry: e, exit: x });

    console.log(`Маршрутов для проверки: ${routes.length}`);

    function parsePage(html) {
        const doc = parser.parseFromString(html, "text/html");
        const tbl = doc.querySelector("#tollresults table");
        if (!tbl) return { error: "нет таблицы" };

        const rows = tbl.querySelectorAll("tbody tr");
        let totalRow = null;
        rows.forEach(r => {
            const t = r.textContent.trim().toLowerCase();
            if (t.startsWith("total")) totalRow = r;
        });
        if (!totalRow) return { error: "нет строки total" };

        const cells = totalRow.querySelectorAll("td");
        const ny = parseFloat(cells[1].textContent.replace(/[$,]/g, ""));
        const nonny = parseFloat(cells[2].textContent.replace(/[$,]/g, ""));

        const distEl = [...doc.querySelectorAll("#tollresults p")].find(p =>
            p.textContent.toLowerCase().includes("approximate distance")
        );

        let miles = null;
        if (distEl) {
            const match = distEl.textContent.match(/([\d.]+)\s*miles/i);
            if (match) miles = parseFloat(match[1]);
        }

        return { ny, nonny, miles, error: null };
    }

    const result = [];
    const BATCH = 500;

    for (let i = 0; i < routes.length; i += BATCH) {
        const batch = routes.slice(i, i + BATCH);

        const jobs = batch.map(async r => {
            const url = `${baseUrl}?Class=${vehicleClass}&Entry=${r.entry}&Exit=${r.exit}`;
            try {
                const html = await (await fetch(url)).text();
                const p = parsePage(html);
                return { ...r, ...p, status: p.error ? "ERR" : "OK" };
            } catch (e) {
                return { ...r, error: e.message, status: "FAIL" };
            }
        });

        const out = await Promise.all(jobs);
        result.push(...out);
        console.log(`Готово ${result.length}/${routes.length}`);
        await new Promise(r => setTimeout(r, 250));
    }

    const ok = result.filter(x => x.status === "OK");

    const json = {
        state: "New York",
        road: "NYS Thruway",
        vehicle_class: vehicleClass,
        total_checked: result.length,
        total_success: ok.length,
        tolls: ok
    };

    console.log("=== FINAL JSON ===");
    console.log(JSON.stringify(json, null, 2));
})();