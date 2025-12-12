-- SQL запросы для удаления CalculatePrice и TollPrice по bounding box
-- Замените координаты bounding box на нужные значения

-- ============================================
-- ЗАПРОС 1: ПРОСМОТР того, что будет удалено
-- ============================================

-- Параметры bounding box (замените на нужные значения)
-- Пример для Maine: minLon = -71.0, minLat = 43.0, maxLon = -69.0, maxLat = 45.0
WITH bounding_box AS (
    SELECT 
        ST_MakeEnvelope(
            -71.0,  -- minLongitude (west)
            43.0,   -- minLatitude (south)
            -69.0,  -- maxLongitude (east)
            45.0,   -- maxLatitude (north)
            4326    -- SRID
        ) AS geom
),
tolls_in_box AS (
    SELECT t."Id", t."Name", t."Key", t."Number", t."Location"
    FROM "Tolls" t, bounding_box bb
    WHERE t."Location" IS NOT NULL
        AND ST_Contains(bb.geom, t."Location")
),
calculate_prices_to_delete AS (
    SELECT DISTINCT cp."Id", cp."StateCalculatorId", cp."FromId", cp."ToId"
    FROM "CalculatePrices" cp
    INNER JOIN tolls_in_box from_toll ON from_toll."Id" = cp."FromId"
    INNER JOIN tolls_in_box to_toll ON to_toll."Id" = cp."ToId"
),
toll_prices_to_delete AS (
    -- TollPrice связанные напрямую с толлами в bounding box
    SELECT DISTINCT tp."Id", tp."TollId", tp."CalculatePriceId", tp."PaymentType", tp."Amount"
    FROM "TollPrices" tp
    INNER JOIN tolls_in_box t ON t."Id" = tp."TollId"
    WHERE tp."TollId" IS NOT NULL
    
    UNION
    
    -- TollPrice связанные через CalculatePrice
    SELECT DISTINCT tp."Id", tp."TollId", tp."CalculatePriceId", tp."PaymentType", tp."Amount"
    FROM "TollPrices" tp
    INNER JOIN calculate_prices_to_delete cp ON cp."Id" = tp."CalculatePriceId"
    WHERE tp."CalculatePriceId" IS NOT NULL
)
SELECT 
    'CalculatePrice' AS entity_type,
    COUNT(*) AS count_to_delete,
    json_agg(
        json_build_object(
            'Id', cp."Id",
            'FromId', cp."FromId",
            'ToId', cp."ToId",
            'StateCalculatorId', cp."StateCalculatorId"
        )
    ) AS details
FROM calculate_prices_to_delete cp

UNION ALL

SELECT 
    'TollPrice' AS entity_type,
    COUNT(*) AS count_to_delete,
    json_agg(
        json_build_object(
            'Id', tp."Id",
            'TollId', tp."TollId",
            'CalculatePriceId', tp."CalculatePriceId",
            'PaymentType', tp."PaymentType",
            'Amount', tp."Amount"
        )
    ) AS details
FROM toll_prices_to_delete tp;

-- ============================================
-- ЗАПРОС 2: УДАЛЕНИЕ записей
-- ============================================

-- ВНИМАНИЕ: Этот запрос удалит данные! Выполняйте только после проверки запросом 1!

-- Сначала удаляем TollPrice (из-за внешних ключей)
WITH bounding_box AS (
    SELECT 
        ST_MakeEnvelope(
            -71.0,  -- minLongitude (west)
            43.0,   -- minLatitude (south)
            -69.0,  -- maxLongitude (east)
            45.0,   -- maxLatitude (north)
            4326    -- SRID
        ) AS geom
),
tolls_in_box AS (
    SELECT t."Id"
    FROM "Tolls" t, bounding_box bb
    WHERE t."Location" IS NOT NULL
        AND ST_Contains(bb.geom, t."Location")
),
calculate_prices_to_delete AS (
    SELECT DISTINCT cp."Id"
    FROM "CalculatePrices" cp
    INNER JOIN tolls_in_box from_toll ON from_toll."Id" = cp."FromId"
    INNER JOIN tolls_in_box to_toll ON to_toll."Id" = cp."ToId"
)
-- Удаляем TollPrice связанные через CalculatePrice
DELETE FROM "TollPrices" tp
WHERE tp."CalculatePriceId" IN (SELECT "Id" FROM calculate_prices_to_delete);

-- Удаляем TollPrice связанные напрямую с толлами
WITH bounding_box AS (
    SELECT 
        ST_MakeEnvelope(
            -71.0,  -- minLongitude (west)
            43.0,   -- minLatitude (south)
            -69.0,  -- maxLongitude (east)
            45.0,   -- maxLatitude (north)
            4326    -- SRID
        ) AS geom
),
tolls_in_box AS (
    SELECT t."Id"
    FROM "Tolls" t, bounding_box bb
    WHERE t."Location" IS NOT NULL
        AND ST_Contains(bb.geom, t."Location")
)
DELETE FROM "TollPrices" tp
WHERE tp."TollId" IN (SELECT "Id" FROM tolls_in_box);

-- Затем удаляем CalculatePrice
WITH bounding_box AS (
    SELECT 
        ST_MakeEnvelope(
            -71.0,  -- minLongitude (west)
            43.0,   -- minLatitude (south)
            -69.0,  -- maxLongitude (east)
            45.0,   -- maxLatitude (north)
            4326    -- SRID
        ) AS geom
),
tolls_in_box AS (
    SELECT t."Id"
    FROM "Tolls" t, bounding_box bb
    WHERE t."Location" IS NOT NULL
        AND ST_Contains(bb.geom, t."Location")
)
DELETE FROM "CalculatePrices" cp
WHERE cp."FromId" IN (SELECT "Id" FROM tolls_in_box)
    AND cp."ToId" IN (SELECT "Id" FROM tolls_in_box);

-- ============================================
-- АЛЬТЕРНАТИВНЫЙ ЗАПРОС 2: Удаление в транзакции
-- ============================================

-- Если нужно выполнить все удаления в одной транзакции:
BEGIN;

WITH bounding_box AS (
    SELECT 
        ST_MakeEnvelope(
            -71.0,  -- minLongitude (west)
            43.0,   -- minLatitude (south)
            -69.0,  -- maxLongitude (east)
            45.0,   -- maxLatitude (north)
            4326    -- SRID
        ) AS geom
),
tolls_in_box AS (
    SELECT t."Id"
    FROM "Tolls" t, bounding_box bb
    WHERE t."Location" IS NOT NULL
        AND ST_Contains(bb.geom, t."Location")
),
calculate_prices_to_delete AS (
    SELECT DISTINCT cp."Id"
    FROM "CalculatePrices" cp
    INNER JOIN tolls_in_box from_toll ON from_toll."Id" = cp."FromId"
    INNER JOIN tolls_in_box to_toll ON to_toll."Id" = cp."ToId"
)
-- 1. Удаляем TollPrice связанные через CalculatePrice
DELETE FROM "TollPrices" tp
WHERE tp."CalculatePriceId" IN (SELECT "Id" FROM calculate_prices_to_delete);

-- 2. Удаляем TollPrice связанные напрямую с толлами
DELETE FROM "TollPrices" tp
WHERE tp."TollId" IN (SELECT "Id" FROM tolls_in_box);

-- 3. Удаляем CalculatePrice
DELETE FROM "CalculatePrices" cp
WHERE cp."FromId" IN (SELECT "Id" FROM tolls_in_box)
    AND cp."ToId" IN (SELECT "Id" FROM tolls_in_box);

-- Проверьте результат перед коммитом:
-- SELECT * FROM "CalculatePrices" WHERE ... (проверьте что удалено)
-- SELECT * FROM "TollPrices" WHERE ... (проверьте что удалено)

-- Если все правильно, выполните:
COMMIT;
-- Если нужно откатить:
-- ROLLBACK;

