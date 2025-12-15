(async () => {
    // 1. Наша фиксированная точка входа
    const fixedEntry = "Westpoint";

    // 2. Список всех точек
    const barriers = [
        { name: "Westpoint", id: "0001" },
        { name: "Calumet E/B Entry", id: "0005" },
        { name: "Calumet W/B", id: "0006" },
        { name: "Cline (Gary) E/W", id: "0010" },
        { name: "Gary East", id: "0017" },
        { name: "Lake Station", id: "0021" },
        { name: "Portage / Willow Creek", id: "0024" },
        { name: "Valparaiso/Chesterton", id: "0031" },
        { name: "Michigan City", id: "0039" },
        { name: "LaPorte", id: "0049" },
        { name: "South Bend West", id: "0072" },
        { name: "South Bend Notre Dame", id: "0077" },
        { name: "Mishawaka", id: "0083" },
        { name: "Elkhart", id: "0092" },
        { name: "Elkhart East", id: "0096" },
        { name: "Bristol/Goshen", id: "0101" },
        { name: "Middlebury", id: "0107" },
        { name: "Howe/LaGrange", id: "0121" },
        { name: "Angola", id: "0144" },
        { name: "Eastpoint", id: "0153" }
    ];

    const results = [];
    const url = '/wp-admin/admin-ajax.php';

    // Функция задержки
    const sleep = (ms) => new Promise(r => setTimeout(r, ms));

    // Функция запроса цены
    async function getPrice(entry, exit, paymentType) {
        const params = new URLSearchParams({
            action: 'get_toll_rate',
            axle_class: '5',
            entry_barrier: entry,
            exit_barrier: exit,
            payment_type: paymentType,
        });

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: params.toString()
            });
            const text = await response.text();
            let price = text.replace(/"/g, '').trim();
            return price || "N/A";
        } catch (e) {
            return "Error";
        }
    }

    console.log(Начинаем сбор цен для входа: ${ fixedEntry }...);

    // 3. Перебираем только выходы
    for (let i = 0; i < barriers.length; i++) {
        const exitPoint = barriers[i].name;

        // Пропускаем, если выход - это и есть наш вход (Westpoint -> Westpoint)
        if (exitPoint === fixedEntry) continue;

        console.log(Запрос: ${ fixedEntry } -> ${ exitPoint });

        const [cashPrice, aviPrice] = await Promise.all([
            getPrice(fixedEntry, exitPoint, 'CASH'),
            getPrice(fixedEntry, exitPoint, 'AVI')
        ]);

        results.push({
            entry: fixedEntry,
            exit: exitPoint,
            cash_rate: cashPrice,
            avi_rate: aviPrice
        });

        // Небольшая пауза
        await sleep(150);
    }

    console.log("Готово! Результат:");
    console.log(JSON.stringify(results, null, 2));
    console.table(results);
})();