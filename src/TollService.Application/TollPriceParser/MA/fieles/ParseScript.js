(async function scrapeEZDriveMA() {
    const BASE_URL = "https://www.ezdrivema.com/TollCalculator";
    const parser = new DOMParser();

    const AXLES = "5";        // 5 Axle
    const PAYMETHOD = "1";    // pay-by-plateMA

    /* ===============================
       ENTRY / EXIT LIST
    =============================== */

    const ENTRIES = [
        "Entry_1", "Entry_2", "Entry_3", "Entry_4", "Entry_5", "Entry_6", "Entry_7", "Entry_8", "Entry_9",
        "Entry_10", "Entry_10A", "Entry_11", "Entry_11A", "Entry_12", "Entry_13", "Entry_14WB", "Entry_15EB",
        "Entry_16EB", "Entry_17", "Entry_18WB", "Entry_19", "Entry_20EB", "Entry_21WB", "Entry_22WB",
        "Entry_22AWB", "Entry_23WB", "Entry_24", "Entry_25", "Entry_26"
    ];

    const EXITS = [
        "Exit_1WB", "Exit_2", "Exit_3", "Exit_4", "Exit_5", "Exit_6", "Exit_7", "Exit_8", "Exit_9",
        "Exit_10", "Exit_10A", "Exit_11", "Exit_11A", "Exit_12", "Exit_13", "Exit_14EB", "Exit_15WB",
        "Exit_16WB", "Exit_17", "Exit_18EB", "Exit_19", "Exit_20WB", "Exit_21_NX", "Exit_22EB",
        "Exit_22A_NX", "Exit_23_NX", "Exit_24", "Exit_25", "Exit_26"
    ];

    /* ===============================
       LOAD VIEWSTATE
    =============================== */

    async function loadState() {
        const html = await (await fetch(BASE_URL, { credentials: "include" })).text();
        const doc = parser.parseFromString(html, "text/html");

        const val = id => doc.querySelector(`input[name="${id}"]`)?.value;

        return {
            __VIEWSTATE: val("__VIEWSTATE"),
            __VIEWSTATEGENERATOR: val("__VIEWSTATEGENERATOR"),
            __EVENTVALIDATION: val("__EVENTVALIDATION"),
            __RequestVerificationToken: val("__RequestVerificationToken")
        };
    }

    /* ===============================
       PARSE RESULT
    =============================== */

    function parseResult(html) {
        const doc = parser.parseFromString(html, "text/html");
        const get = id => doc.querySelector(`#${id}`)?.textContent.trim() || null;

        return {
            entryText: get("dnn_ctr1341_View_lblEntry"),
            exitText: get("dnn_ctr1341_View_lblExit"),
            axlesText: get("dnn_ctr1341_View_lblAxles"),
            paymentText: get("dnn_ctr1341_View_lblPaymentMethod"),

            eastbound: {
                toll: get("dnn_ctr1341_View_lblTollEB"),
                mileage: get("dnn_ctr1341_View_lblMileageEB"),
                time: get("dnn_ctr1341_View_lblTimeEB")
            },

            westbound: {
                toll: get("dnn_ctr1341_View_lblTollWB"),
                mileage: get("dnn_ctr1341_View_lblMileageWB"),
                time: get("dnn_ctr1341_View_lblTimeWB")
            }
        };
    }

    /* ===============================
       POST REQUEST
    =============================== */

    async function fetchRoute(state, entry, exit) {
        const form = new FormData();

        Object.entries(state).forEach(([k, v]) => form.append(k, v));

        form.append("dnn$ctr1341$View$ddlEntry", entry);
        form.append("dnn$ctr1341$View$ddlExit", exit);
        form.append("dnn$ctr1341$View$ddlAxleType", AXLES);
        form.append("dnn$ctr1341$View$ddlPaymethod", PAYMETHOD);
        form.append("dnn$ctr1341$View$btnSubmit", "Submit");

        const html = await (await fetch(BASE_URL, {
            method: "POST",
            body: form,
            credentials: "include"
        })).text();

        return parseResult(html);
    }

    /* ===============================
       BUILD ROUTES
    =============================== */

    const routes = [];
    for (const e of ENTRIES)
        for (const x of EXITS)
            if (e !== x)
                routes.push({ entry: e, exit: x });

    console.log(`Routes to check: ${routes.length}`);

    const state = await loadState();
    const results = [];

    const BATCH = 100;
    const DELAY_BETWEEN_BATCHES = 500;

    /* ===============================
       MAIN LOOP
    =============================== */

    for (let i = 0; i < routes.length; i += BATCH) {
        const batch = routes.slice(i, i + BATCH);

        const jobs = batch.map(async r => {
            try {
                const data = await fetchRoute(state, r.entry, r.exit);

                return {
                    EntryNumber: r.entry,
                    ExitNumber: r.exit,

                    entry: data.entryText,
                    exit: data.exitText,

                    axles: data.axlesText || "5 Axle",
                    payment: data.paymentText || "pay-by-plate",

                    eastbound: data.eastbound,
                    westbound: data.westbound,

                    status: "OK"
                };
            } catch (e) {
                return {
                    EntryNumber: r.entry,
                    ExitNumber: r.exit,
                    status: "ERR",
                    error: e.message
                };
            }
        });

        const out = await Promise.all(jobs);
        results.push(...out);

        console.log(`Progress ${results.length}/${routes.length}`);
        await new Promise(r => setTimeout(r, DELAY_BETWEEN_BATCHES));
    }

    /* ===============================
       FINAL JSON
    =============================== */

    const json = {
        state: "Massachusetts",
        road: "Massachusetts Turnpike",
        total: results.length,
        ok: results.filter(x => x.status === "OK").length,
        data: results
    };

    console.log("=== FINAL JSON ===");
    console.log(JSON.stringify(json, null, 2));
})();
