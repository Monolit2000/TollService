/**
 * Версия 5: Финальный скрипт для парсинга тарифов US 301.
 * ИСПРАВЛЕНО: Обновлен селектор для поиска таблицы тарифов.
 */
async function scrapeAllUS301TollsV5(vehicleClass = 5) {
  const url = "https://deldot.gov/public.ejs?command=PublicTollRateUS301";

  // --- Исходные данные точек въезда/выезда ---
  const entryPoints = [
    { id: "121", label: "Northbound - DE/MD Stateline" },
    { id: "122", label: "Northbound - Levels Rd" },
    { id: "132", label: "Southbound - Levels Rd" },
    { id: "123", label: "Northbound - Summit Bridge Road" },
    { id: "131", label: "Southbound - Summit Bridge Road" },
    { id: "124", label: "Northbound - Jamison Corner Road" },
    { id: "130", label: "Southbound - Jamison Corner Road" },
    { id: "129", label: "Southbound - SR 1" },
  ];
  const exitPoints = [
    { id: "136", label: "Southbound - DE/MD Stateline" },
    { id: "135", label: "Southbound - Levels Rd" },
    { id: "134", label: "Southbound - Summit Bridge Road" },
    { id: "133", label: "Southbound - Jamison Corner Road" },
    { id: "125", label: "Northbound - Levels Rd" },
    { id: "126", label: "Northbound - Summit Bridge Road" },
    { id: "127", label: "Northbound - Jamison Corner Road" },
    { id: "128", label: "Northbound - SR 1" },
  ];

  const possibleRoutes = [];
  for (const entry of entryPoints) {
    for (const exit of exitPoints) {
      if (entry.id !== exit.id) {
        possibleRoutes.push({
          entry: entry.id,
          exit: exit.id,
          entry_label: entry.label,
          exit_label: exit.label,
        });
      }
    }
  }

  const allResults = [];
  const parser = new DOMParser();

  /**
   * Парсит HTML и извлекает цены E-ZPass и Video (Cash).
   * Использует специфический селектор на основе предоставленного HTML.
   */
  function parseTollHtml(htmlText) {
    const doc = parser.parseFromString(htmlText, "text/html");

    // 1. Проверка на сообщение об ошибке (.errorMsg)
    const errorMsg = doc.querySelector(".errorMsg");
    if (errorMsg) {
      return { ez_pass: null, cash: null, error: errorMsg.textContent.trim() };
    }

    // 2. Ищем контейнер Toll Rates (ближайший родитель для таблицы)
    // В предоставленном HTML таблица Rate находится внутри div.col-md-6.well, после h4 "Toll Rates"
    const tollRatesContainer = doc.querySelector(".col-md-6.well");

    if (!tollRatesContainer) {
      return {
        ez_pass: null,
        cash: null,
        error: "Toll rates container not found.",
      };
    }

    // 3. Ищем первую таблицу с классом table-condensed внутри контейнера
    const table = tollRatesContainer.querySelector(
      ".table.table-condensed:first-of-type"
    );

    if (!table) {
      // Если таблица не найдена, это значит, что маршрут невалиден, или цены отсутствуют.
      return {
        ez_pass: null,
        cash: null,
        error: "Toll rates table not found inside container.",
      };
    }

    // 4. Извлекаем цены
    const ezPassRow = table.querySelector("tr:nth-child(2)"); // E-ZPass - 2-я строка
    const videoRow = table.querySelector("tr:nth-child(3)"); // Video - 3-я строка

    if (!ezPassRow || !videoRow) {
      return {
        ez_pass: null,
        cash: null,
        error: "E-ZPass or Video row not found.",
      };
    }

    try {
      // E-ZPass: второй td в строке
      const ezPassCell = ezPassRow.querySelectorAll("td")[1];
      // Video: второй td в строке
      const videoCell = videoRow.querySelectorAll("td")[1];

      const ezPassPriceText = ezPassCell.textContent
        .trim()
        .replace("$", "")
        .replace(",", "");
      const cashPriceText = videoCell.textContent
        .trim()
        .replace("$", "")
        .replace(",", "");

      const ezPassPrice = parseFloat(ezPassPriceText) || 0;
      const cashPrice = parseFloat(cashPriceText) || 0;

      if (isNaN(ezPassPrice) || isNaN(cashPrice)) {
        return { ez_pass: null, cash: null, error: "Parsed price is NaN." };
      }

      return { ez_pass: ezPassPrice, cash: cashPrice, error: null };
    } catch (e) {
      return {
        ez_pass: null,
        cash: null,
        error: `Failed to parse price values: ${e.message}`,
      };
    }
  }

  // --- Запуск всех запросов ---
  console.log(
    `Начинаем парсинг ${possibleRoutes.length} маршрутов для класса ТС ${vehicleClass}...`
  );

  const BATCH_SIZE = 10;

  for (let i = 0; i < possibleRoutes.length; i += BATCH_SIZE) {
    const batch = possibleRoutes.slice(i, i + BATCH_SIZE);

    const promises = batch.map(async (route) => {
      const formData = new URLSearchParams();
      formData.append("entry", route.entry);
      formData.append("exit", route.exit);
      formData.append("vehicle", vehicleClass);

      try {
        const response = await fetch(url, {
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
          },
          body: formData.toString(),
        });

        if (!response.ok) {
          throw new Error(`HTTP Error! Status: ${response.status}`);
        }

        const htmlText = await response.text();
        const prices = parseTollHtml(htmlText);

        return {
          entry: route.entry,
          exit: route.exit,
          entry_label: route.entry_label,
          exit_label: route.exit_label,
          ez_pass: prices.ez_pass,
          cash: prices.cash,
          status: prices.error ? "Error" : "OK",
          message: prices.error,
        };
      } catch (error) {
        return {
          entry: route.entry,
          exit: route.exit,
          entry_label: route.entry_label,
          exit_label: route.exit_label,
          ez_pass: null,
          cash: null,
          status: "Request Failed",
          message: error.message,
        };
      }
    });

    const batchResults = await Promise.all(promises);
    allResults.push(...batchResults);

    console.log(
      `Обработано маршрутов: ${allResults.length}/${possibleRoutes.length}`
    );

    await new Promise((r) => setTimeout(r, 200));
  }

  const successful_rates = allResults.filter((r) => r.status === "OK");

  const finalTollData = {
    state: "Delaware",
    road: "US 301",
    vehicle_class_id: vehicleClass,
    description: "5-Axle Truck",
    total_routes_checked: allResults.length,
    toll_rates: successful_rates,
  };

  console.log("--- ФИНАЛЬНЫЙ JSON (Успешные тарифы) ---");
  console.log(JSON.stringify(finalTollData, null, 4));

  return finalTollData;
}

// --- Запуск ---
// Запустите эту функцию в консоли и скопируйте вывод "ФИНАЛЬНЫЙ JSON".
scrapeAllUS301TollsV5(5);
