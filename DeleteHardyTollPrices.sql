-- ============================================
-- 1. ПРОСМОТР: Найти цены толлов из prices_Hardy.json
-- ============================================
-- Этот запрос показывает все цены толлов, которые будут удалены
-- Имена из prices_Hardy.json:
-- - Tidwell Road
-- - Little York
-- - Aldine Mail Route
-- - Hardy South Plaza
-- - Central Greens Blvd
-- - Airport Connector
-- - Rankin Road
-- - Richey Road
-- - FM 1960
-- - Hardy North Plaza

SELECT 
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    ST_AsText(t."Location") AS Location,
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."Description",
    tp."CalculatePriceId"
FROM "Tolls" t
INNER JOIN "TollPrices" tp ON tp."TollId" = t."Id"
WHERE 
    -- Ищем толлы по именам из prices_Hardy.json
    t."Name" IN (
        'Tidwell Road',
        'Little York',
        'Aldine Mail Route',
        'Hardy South Plaza',
        'Central Greens Blvd',
        'Airport Connector',
        'Rankin Road',
        'Richey Road',
        'FM 1960',
        'Hardy North Plaza'
    )
    -- Фильтруем по bounding box Техаса (25.8, -106.6, 36.5, -93.5)
    AND t."Location" IS NOT NULL
    AND ST_X(t."Location") BETWEEN -106.6 AND -93.5  -- longitude
    AND ST_Y(t."Location") BETWEEN 25.8 AND 36.5      -- latitude
    -- Фильтруем только цены, созданные парсером (с описанием, содержащим "Texas")
    AND (tp."Description" LIKE 'Texas%' OR tp."Description" IS NULL)
    -- Исключаем цены, связанные с CalculatePrice
    AND tp."CalculatePriceId" IS NULL
ORDER BY t."Name", tp."PaymentType", tp."AxelType";

-- ============================================
-- 2. УДАЛЕНИЕ: Удалить цены толлов из prices_Hardy.json
-- ============================================
-- ВНИМАНИЕ: Этот запрос удалит все найденные цены!
-- Сначала выполните запрос выше, чтобы убедиться, что удаляете правильные данные.

DELETE FROM "TollPrices"
WHERE "Id" IN (
    SELECT tp."Id"
    FROM "Tolls" t
    INNER JOIN "TollPrices" tp ON tp."TollId" = t."Id"
    WHERE 
        -- Ищем толлы по именам из prices_Hardy.json
        t."Name" IN (
            'Tidwell Road',
            'Little York',
            'Aldine Mail Route',
            'Hardy South Plaza',
            'Central Greens Blvd',
            'Airport Connector',
            'Rankin Road',
            'Richey Road',
            'FM 1960',
            'Hardy North Plaza'
        )
        -- Фильтруем по bounding box Техаса (25.8, -106.6, 36.5, -93.5)
        AND t."Location" IS NOT NULL
        AND ST_X(t."Location") BETWEEN -106.6 AND -93.5  -- longitude
        AND ST_Y(t."Location") BETWEEN 25.8 AND 36.5      -- latitude
        -- Фильтруем только цены, созданные парсером (с описанием, содержащим "Texas")
        AND (tp."Description" LIKE 'Texas%' OR tp."Description" IS NULL)
        -- Исключаем цены, связанные с CalculatePrice
        AND tp."CalculatePriceId" IS NULL
);

-- ============================================
-- Альтернативный вариант: Удаление по частичному совпадению имен
-- ============================================
-- Если имена в базе немного отличаются, можно использовать LIKE

-- ПРОСМОТР с частичным совпадением:
/*
SELECT 
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."Description"
FROM "Tolls" t
INNER JOIN "TollPrices" tp ON tp."TollId" = t."Id"
WHERE 
    (
        t."Name" ILIKE '%Tidwell%' OR
        t."Name" ILIKE '%Little York%' OR
        t."Name" ILIKE '%Aldine%' OR
        t."Name" ILIKE '%Hardy South%' OR
        t."Name" ILIKE '%Hardy North%' OR
        t."Name" ILIKE '%Central Greens%' OR
        t."Name" ILIKE '%Airport Connector%' OR
        t."Name" ILIKE '%Rankin%' OR
        t."Name" ILIKE '%Richey%' OR
        t."Name" ILIKE '%FM 1960%'
    )
    AND t."Location" IS NOT NULL
    AND ST_X(t."Location") BETWEEN -106.6 AND -93.5
    AND ST_Y(t."Location") BETWEEN 25.8 AND 36.5
    AND (tp."Description" LIKE 'Texas%' OR tp."Description" IS NULL)
    AND tp."CalculatePriceId" IS NULL
ORDER BY t."Name", tp."PaymentType", tp."AxelType";
*/

