-- SQL запросы для просмотра и удаления TollPrice с AxelType = 0 для толлов в bounding box
-- Замените координаты bounding box на нужные значения

-- Параметры bounding box (замените на нужные значения)
-- Пример для Maine: minLon = -71.0, minLat = 43.0, maxLon = -69.0, maxLat = 45.0

-- ============================================
-- ЗАПРОС 1: ПРОСМОТР TollPrice с AxelType = 0
-- ============================================

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
calculate_prices_in_box AS (
    SELECT DISTINCT cp."Id"
    FROM "CalculatePrices" cp
    INNER JOIN tolls_in_box from_toll ON from_toll."Id" = cp."FromId"
    INNER JOIN tolls_in_box to_toll ON to_toll."Id" = cp."ToId"
)
SELECT 
    tp."Id",
    tp."TollId",
    tp."CalculatePriceId",
    tp."PaymentType",
    tp."AxelType",
    tp."Amount",
    tp."Description"
FROM "TollPrices" tp
INNER JOIN calculate_prices_in_box cp ON cp."Id" = tp."CalculatePriceId"
WHERE tp."AxelType" = 0;

-- ============================================
-- ЗАПРОС 2: УДАЛЕНИЕ TollPrice с AxelType = 0
-- ============================================

-- ВНИМАНИЕ: Этот запрос удалит данные! Выполняйте только после проверки запросом 1!

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
calculate_prices_in_box AS (
    SELECT DISTINCT cp."Id"
    FROM "CalculatePrices" cp
    INNER JOIN tolls_in_box from_toll ON from_toll."Id" = cp."FromId"
    INNER JOIN tolls_in_box to_toll ON to_toll."Id" = cp."ToId"
)
DELETE FROM "TollPrices" tp
USING calculate_prices_in_box cp
WHERE tp."CalculatePriceId" = cp."Id"
    AND tp."AxelType" = 0;

