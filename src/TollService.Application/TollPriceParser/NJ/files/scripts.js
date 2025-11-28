/**
 * Скрипт для парсинга тарифов New Jersey Turnpike (NJTA).
 * Запускать в консоли браузера на сайте https://www.njta.gov/
 */
async function scrapeNJTurnpikeTolls(vehicleClass = 5) {
  const baseUrl = "https://www.njta.gov/wp-json/njta/v1/partials";

  // 1. Список точек (извлечен из вашего HTML select)
  const interchanges = [
    { id: "01", name: "01: DELAWARE MEMORIAL BRIDGE" },
    { id: "02", name: "02: US 322, SWEDESBORO, CHESTER" },
    { id: "03", name: "03: NJ 168, WOODBURY, SOUTH CAMDEN" },
    { id: "04", name: "04: NJ 73, CAMDEN, PHILADELPHIA" },
    { id: "05", name: "05: BURLINGTON, MT. HOLLY" },
    { id: "06", name: "06: PENNSYLVANIA TURNPIKE" },
    { id: "07", name: "07: US 206, BORDENTOWN, TRENTON" },
    { id: "07A", name: "07A: I-195, TRENTON, HAMILTON" },
    { id: "08", name: "08: NJ 33, HIGHTSTOWN, FREEHOLD" },
    { id: "08A", name: "08A: CRANBURY, JAMESBURG" },
    { id: "09", name: "09: NJ 18, NEW BRUNSWICK" },
    { id: "10", name: "10: I-287, METUCHEN, PERTH AMBOY" },
    { id: "11", name: "11: GARDEN STATE PARKWAY" },
    { id: "12", name: "12: CARTERET, RAHWAY" },
    { id: "13", name: "13: I-278, ELIZABETH, STATEN ISLAND" },
    { id: "13A", name: "13A: NEWARK AIRPORT, ELIZABETH SEAPORT" },
    { id: "14", name: "14: NEWARK AIRPORT, I-78, US 1 AND 9" },
    { id: "14A", name: "14A: HUDSON CITY EXT, BAYONNE" },
    { id: "14B", name: "14B: JERSEY CITY, LIBERTY ST. PARK" },
    { id: "14C", name: "14C: HOLLAND TUNNEL" },
    { id: "15E", name: "15E: US 1 AND 9, NEWARK, JERSEY CITY" },
    { id: "15W", name: "15W: I-280, NEWARK, HARRISON" },
    { id: "15X", name: "15X: SECAUCUS TRANSFER STATION, SECAUCUS" },
    { id: "16E", name: "16E: NJ 3, LINCOLN TUNNEL, SECAUCUS" },
    { id: "16W", name: "16W: NJ 3, SPORTSPLEX, EAST RUTHERFORD" },
    { id: "18E", name: "18E: GWB, US 46, I-80, RIDGEFIELD PARK" },
    { id: "18W", name: "18W: GWB, US 46, I-80, RIDGEFIELD PARK" },
  ];

  // 2. Генерация маршрутов
  const routes = [];
  for (const entry of interchanges) {
    for (const exit of interchanges) {
      if (entry.id !== exit.id) {
        routes.push({
          entry: entry.id,
          exit: exit.id,
          entry_name: entry.name,
          exit_name: exit.name,
        });
      }
    }
  }

  const results = [];
  const parser = new DOMParser();

  // Функция очистки цены от '$' и преобразования в число
  const parsePrice = (str) => {
    if (!str) return null;
    const clean = str.replace("$", "").replace(",", "").trim();
    return parseFloat(clean) || 0;
  };

  /**
   * Парсер HTML ответа NJTA
   */
  function parseResponse(html) {
    const doc = parser.parseFromString(html, "text/html");

    // Селекторы на основе предоставленного вами HTML
    // Cash находится в .trip-calculation__total -> .trip-calculation__cash-sum -> .trip-calculation__number--total
    const cashEl = doc.querySelector(
      ".trip-calculation__cash-sum .trip-calculation__number--total"
    );

    // Peak находится в .trip-calculation__peak -> .trip-calculation__number
    const peakEl = doc.querySelector(
      ".trip-calculation__peak .trip-calculation__number"
    );

    // Off-Peak находится в .trip-calculation__off-peak -> .trip-calculation__number
    const offPeakEl = doc.querySelector(
      ".trip-calculation__off-peak .trip-calculation__number"
    );

    return {
      cash: cashEl ? parsePrice(cashEl.textContent) : null,
      ez_pass_peak: peakEl ? parsePrice(peakEl.textContent) : null,
      ez_pass_off_peak: offPeakEl ? parsePrice(offPeakEl.textContent) : null,
    };
  }

  console.log(
    `Начинаем обработку ${routes.length} маршрутов для NJ Turnpike (Class ${vehicleClass})...`
  );

  // 3. Выполнение запросов пакетами
  const BATCH_SIZE = 5; // NJTA может блокировать частые запросы, делаем аккуратно

  for (let i = 0; i < routes.length; i += BATCH_SIZE) {
    const batch = routes.slice(i, i + BATCH_SIZE);

    const promises = batch.map(async (route) => {
      // Формируем URL с параметрами
      const params = new URLSearchParams({
        slug: "map/trip-details",
        tab: "turnpike",
        reset: "0",
        "senior-discount": "false",
        "green-discount": "false",
        entrance: route.entry,
        exit: route.exit,
        "vehicle-type": vehicleClass,
      });

      try {
        const response = await fetch(`${baseUrl}?${params.toString()}`, {
          method: "GET",
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const htmlText = await response.text();
        const prices = parseResponse(htmlText);

        // Проверяем, удалось ли найти цены (иногда маршруты недоступны)
        if (prices.cash !== null) {
          return {
            entry: route.entry,
            exit: route.exit,
            entry_name: route.entry_name,
            exit_name: route.exit_name,
            ...prices,
            status: "OK",
          };
        } else {
          return {
            entry: route.entry,
            exit: route.exit,
            status: "No Rates Found",
          };
        }
      } catch (error) {
        console.error(`Ошибка ${route.entry}->${route.exit}:`, error);
        return {
          entry: route.entry,
          exit: route.exit,
          status: "Error",
          message: error.message,
        };
      }
    });

    const batchResults = await Promise.all(promises);
    results.push(...batchResults);

    console.log(`Обработано: ${results.length} / ${routes.length}`);

    // Пауза 300мс между пакетами
    await new Promise((r) => setTimeout(r, 300));
  }

  // 4. Формирование итогового JSON
  const finalData = {
    state: "New Jersey",
    road: "NJ Turnpike",
    vehicle_class_id: vehicleClass,
    description: "5-Axle Truck", // Предполагается, что 5 класс это 5 осей
    total_checked: results.length,
    toll_rates: results.filter((r) => r.status === "OK"),
  };

  console.log("--- ГОТОВЫЙ JSON (СКОПИРУЙТЕ НИЖЕ) ---");
  console.log(JSON.stringify(finalData, null, 4));
}

// Запуск
scrapeNJTurnpikeTolls(5);
