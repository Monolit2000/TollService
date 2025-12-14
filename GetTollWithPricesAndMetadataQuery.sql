-- Запрос для получения информации о толле по имени
-- Выводит: TollPrices, PaymentMethods, WebsiteUrl, цены и аксели

SELECT 
    -- Информация о Toll
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    t."WebsiteUrl",
    
    -- PaymentMethod из Toll (owned entity)
    t."PaymentMethod_Tag",
    t."PaymentMethod_NoPlate",
    t."PaymentMethod_Cash",
    t."PaymentMethod_NoCard",
    t."PaymentMethod_App",
    
    -- TollPrice данные
    tp."Id" AS TollPriceId,
    tp."PaymentType",
    CASE tp."PaymentType"
        WHEN 0 THEN 'Unknown'
        WHEN 1 THEN 'IPass'
        WHEN 2 THEN 'PayOnline'
        WHEN 3 THEN 'Cash'
        WHEN 4 THEN 'EZPass'
        WHEN 5 THEN 'OutOfStateEZPass'
        WHEN 6 THEN 'VideoTolls'
        WHEN 7 THEN 'SunPass'
        WHEN 8 THEN 'AccountToll'
        WHEN 9 THEN 'NonAccountToll'
        ELSE 'Unknown'
    END AS PaymentTypeName,
    tp."Amount",
    tp."AxelType",
    CASE tp."AxelType"
        WHEN 0 THEN 'Unknown'
        WHEN 1 THEN '1L'
        WHEN 2 THEN '2L'
        WHEN 3 THEN '3L'
        WHEN 4 THEN '4L'
        WHEN 5 THEN '5L'
        WHEN 6 THEN '6L'
        WHEN 7 THEN '7L'
        WHEN 8 THEN '8L'
        WHEN 9 THEN '9L'
        ELSE 'Unknown'
    END AS AxelTypeName,
    tp."TimeOfDay",
    CASE tp."TimeOfDay"
        WHEN 0 THEN 'Any'
        WHEN 1 THEN 'Day'
        WHEN 2 THEN 'Night'
        ELSE 'Any'
    END AS TimeOfDayName,
    tp."DayOfWeekFrom",
    CASE tp."DayOfWeekFrom"
        WHEN 0 THEN 'Any'
        WHEN 1 THEN 'Monday'
        WHEN 2 THEN 'Tuesday'
        WHEN 3 THEN 'Wednesday'
        WHEN 4 THEN 'Thursday'
        WHEN 5 THEN 'Friday'
        WHEN 6 THEN 'Saturday'
        WHEN 7 THEN 'Sunday'
        ELSE 'Any'
    END AS DayOfWeekFromName,
    tp."DayOfWeekTo",
    CASE tp."DayOfWeekTo"
        WHEN 0 THEN 'Any'
        WHEN 1 THEN 'Monday'
        WHEN 2 THEN 'Tuesday'
        WHEN 3 THEN 'Wednesday'
        WHEN 4 THEN 'Thursday'
        WHEN 5 THEN 'Friday'
        WHEN 6 THEN 'Saturday'
        WHEN 7 THEN 'Sunday'
        ELSE 'Any'
    END AS DayOfWeekToName,
    tp."TimeFrom",
    tp."TimeTo",
    tp."Description",
    tp."CalculatePriceId",
    CASE 
        WHEN tp."CalculatePriceId" IS NOT NULL THEN true
        ELSE false
    END AS IsCalculate

FROM "Tolls" t
LEFT JOIN "TollPrices" tp ON tp."TollId" = t."Id"

WHERE t."Name" = 'YOUR_TOLL_NAME_HERE'  -- Замените на имя вашего толла
   OR t."Key" = 'YOUR_TOLL_KEY_HERE'    -- Или используйте Key для поиска

ORDER BY 
    tp."PaymentType", 
    tp."AxelType", 
    tp."TimeOfDay",
    tp."DayOfWeekFrom",
    tp."TimeFrom";

-- Альтернативный запрос с группировкой по Toll (если нужно видеть один Toll со всеми ценами в JSON)
SELECT 
    t."Id" AS TollId,
    t."Name" AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    t."WebsiteUrl",
    jsonb_build_object(
        'Tag', t."PaymentMethod_Tag",
        'NoPlate', t."PaymentMethod_NoPlate",
        'Cash', t."PaymentMethod_Cash",
        'NoCard', t."PaymentMethod_NoCard",
        'App', t."PaymentMethod_App"
    ) AS PaymentMethod,
    COALESCE(
        (
            SELECT json_agg(
                jsonb_build_object(
                    'Id', tp."Id",
                    'PaymentType', tp."PaymentType",
                    'PaymentTypeName', CASE tp."PaymentType"
                        WHEN 0 THEN 'Unknown'
                        WHEN 1 THEN 'IPass'
                        WHEN 2 THEN 'PayOnline'
                        WHEN 3 THEN 'Cash'
                        WHEN 4 THEN 'EZPass'
                        WHEN 5 THEN 'OutOfStateEZPass'
                        WHEN 6 THEN 'VideoTolls'
                        WHEN 7 THEN 'SunPass'
                        WHEN 8 THEN 'AccountToll'
                        WHEN 9 THEN 'NonAccountToll'
                        ELSE 'Unknown'
                    END,
                    'Amount', tp."Amount",
                    'AxelType', tp."AxelType",
                    'AxelTypeName', CASE tp."AxelType"
                        WHEN 0 THEN 'Unknown'
                        WHEN 1 THEN '1L'
                        WHEN 2 THEN '2L'
                        WHEN 3 THEN '3L'
                        WHEN 4 THEN '4L'
                        WHEN 5 THEN '5L'
                        WHEN 6 THEN '6L'
                        WHEN 7 THEN '7L'
                        WHEN 8 THEN '8L'
                        WHEN 9 THEN '9L'
                        ELSE 'Unknown'
                    END,
                    'TimeOfDay', tp."TimeOfDay",
                    'TimeOfDayName', CASE tp."TimeOfDay"
                        WHEN 0 THEN 'Any'
                        WHEN 1 THEN 'Day'
                        WHEN 2 THEN 'Night'
                        ELSE 'Any'
                    END,
                    'DayOfWeekFrom', tp."DayOfWeekFrom",
                    'DayOfWeekFromName', CASE tp."DayOfWeekFrom"
                        WHEN 0 THEN 'Any'
                        WHEN 1 THEN 'Monday'
                        WHEN 2 THEN 'Tuesday'
                        WHEN 3 THEN 'Wednesday'
                        WHEN 4 THEN 'Thursday'
                        WHEN 5 THEN 'Friday'
                        WHEN 6 THEN 'Saturday'
                        WHEN 7 THEN 'Sunday'
                        ELSE 'Any'
                    END,
                    'DayOfWeekTo', tp."DayOfWeekTo",
                    'DayOfWeekToName', CASE tp."DayOfWeekTo"
                        WHEN 0 THEN 'Any'
                        WHEN 1 THEN 'Monday'
                        WHEN 2 THEN 'Tuesday'
                        WHEN 3 THEN 'Wednesday'
                        WHEN 4 THEN 'Thursday'
                        WHEN 5 THEN 'Friday'
                        WHEN 6 THEN 'Saturday'
                        WHEN 7 THEN 'Sunday'
                        ELSE 'Any'
                    END,
                    'TimeFrom', tp."TimeFrom"::text,
                    'TimeTo', tp."TimeTo"::text,
                    'Description', tp."Description",
                    'IsCalculate', tp."CalculatePriceId" IS NOT NULL
                )
                ORDER BY tp."PaymentType", tp."AxelType", tp."TimeOfDay"
            )
            FROM "TollPrices" tp
            WHERE tp."TollId" = t."Id"
        ),
        '[]'::json
    ) AS TollPrices

FROM "Tolls" t

WHERE t."Name" = 'YOUR_TOLL_NAME_HERE'  -- Замените на имя вашего толла
   OR t."Key" = 'YOUR_TOLL_KEY_HERE';   -- Или используйте Key для поиска
