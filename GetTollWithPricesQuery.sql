SELECT 
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    t."Price" AS TollPrice,
    t."Location",
    t."RoadId",
    t."StateCalculatorId",
    t."isDynamic",
    
    -- TollPrice данные
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."TimeOfDay",
    tp."DayOfWeekFrom",
    tp."DayOfWeekTo",
    tp."TimeFrom",
    tp."TimeTo",
    tp."Description",
    tp."CalculatePriceId"

FROM "Tolls" t
LEFT JOIN "TollPrices" tp ON tp."TollId" = t."Id"

WHERE t."Name" = '165-Norhbound'

ORDER BY tp."PaymentType", tp."AxelType", tp."TimeOfDay";


// Next query
SELECT 
    cp."Id" AS CalculatePriceId,
    cp."StateCalculatorId",
    cp."Online",
    cp."IPass",
    cp."Cash",
    t."Id" AS FromTollId,
    t."Name" AS FromTollName,
    t."Key" AS FromTollKey,
    t."Number" AS FromTollNumber,
    to_toll."Id" AS ToTollId,
    to_toll."Name" AS ToTollName,
    to_toll."Key" AS ToTollKey,
    to_toll."Number" AS ToTollNumber,
    sc."Name" AS StateCalculatorName,
    sc."StateCode",
    COALESCE(
        (
            SELECT json_agg(
                jsonb_build_object(
                    'Id', tp."Id",
                    'PaymentType', tp."PaymentType",
                    'Amount', tp."Amount",
                    'AxelType', tp."AxelType",
                    'TimeOfDay', tp."TimeOfDay",
                    'DayOfWeekFrom', tp."DayOfWeekFrom",
                    'DayOfWeekTo', tp."DayOfWeekTo",
                    'TimeFrom', tp."TimeFrom",
                    'TimeTo', tp."TimeTo",
                    'Description', tp."Description"
                )
            )
            FROM "TollPrices" tp
            WHERE tp."CalculatePriceId" = cp."Id"
        ),
        '[]'::json
    ) AS TollPrices

FROM "Tolls" t
INNER JOIN "CalculatePrices" cp ON cp."FromId" = t."Id"
INNER JOIN "Tolls" to_toll ON to_toll."Id" = cp."ToId"
LEFT JOIN "StateCalculators" sc ON sc."Id" = cp."StateCalculatorId"

WHERE t."Name" = '93-Northbound'

ORDER BY cp."Id";

-- Next query
SELECT 
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."TimeOfDay",
    tp."DayOfWeekFrom",
    tp."DayOfWeekTo",
    tp."TimeFrom",
    tp."TimeTo",
    tp."Description"

FROM "Tolls" t
INNER JOIN "TollPrices" tp ON tp."TollId" = t."Id"

WHERE t."Name" = '165-Northbound'
    AND tp."CalculatePriceId" IS NULL

ORDER BY tp."PaymentType", tp."AxelType", tp."TimeOfDay";

-- Next query from to toll prices
SELECT 
    -- Информация о CalculatePrice
    cp."Id" AS CalculatePriceId,
    cp."StateCalculatorId",
    cp."Online",
    cp."IPass",
    cp."Cash",
    
    -- Информация о From Toll
    from_toll."Id" AS FromTollId,
    from_toll."Name" AS FromTollName,
    from_toll."Key" AS FromTollKey,
    from_toll."Number" AS FromTollNumber,
    
    -- Информация о To Toll
    to_toll."Id" AS ToTollId,
    to_toll."Name" AS ToTollName,
    to_toll."Key" AS ToTollKey,
    to_toll."Number" AS ToTollNumber,
    
    -- Информация о StateCalculator
    sc."Id" AS StateCalculatorId,
    sc."Name" AS StateCalculatorName,
    sc."StateCode",
    
    -- TollPrice данные
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."TimeOfDay",
    tp."DayOfWeekFrom",
    tp."DayOfWeekTo",
    tp."TimeFrom",
    tp."TimeTo",
    tp."Description",
    tp."TollId" AS TollPriceTollId

FROM "CalculatePrices" cp
INNER JOIN "Tolls" from_toll ON from_toll."Id" = cp."FromId"
INNER JOIN "Tolls" to_toll ON to_toll."Id" = cp."ToId"
LEFT JOIN "StateCalculators" sc ON sc."Id" = cp."StateCalculatorId"
LEFT JOIN "TollPrices" tp ON tp."CalculatePriceId" = cp."Id"

WHERE from_toll."Name" = 'FromTollName'
    AND to_toll."Name" = 'ToTollName'

ORDER BY tp."PaymentType", tp."AxelType", tp."TimeOfDay";