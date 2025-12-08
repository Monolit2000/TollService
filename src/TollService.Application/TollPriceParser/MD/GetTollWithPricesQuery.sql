-- SQL запрос для получения Toll с его CalculatePrice и ценами по имени
-- Замените 'Toll Name' на нужное имя toll

-- Вариант 1: Простой запрос - все данные в плоском виде
SELECT 
    t.Id AS TollId,
    t.Name AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    t."StateCalculatorId",
    sc.Name AS StateCalculatorName,
    sc."StateCode",
    
    -- CalculatePrice где toll является From
    cp_from.Id AS CP_Id_AsFrom,
    cp_from."ToId" AS CP_ToId_AsFrom,
    t_to_from.Name AS CP_ToName_AsFrom,
    cp_from."StateCalculatorId" AS CP_StateCalculatorId_AsFrom,
    
    -- CalculatePrice где toll является To
    cp_to.Id AS CP_Id_AsTo,
    cp_to."FromId" AS CP_FromId_AsTo,
    t_from_to.Name AS CP_FromName_AsTo,
    cp_to."StateCalculatorId" AS CP_StateCalculatorId_AsTo,
    
    -- TollPrice
    tp.Id AS TollPriceId,
    tp."CalculatePriceId",
    tp."PaymentType",
    tp."Amount",
    tp."AxelType",
    tp."Description",
    CASE 
        WHEN tp."CalculatePriceId" IS NOT NULL THEN 'CalculatePrice'
        ELSE 'Direct'
    END AS PriceType

FROM "Tolls" t
LEFT JOIN "StateCalculators" sc ON t."StateCalculatorId" = sc.Id
LEFT JOIN "CalculatePrices" cp_from ON cp_from."FromId" = t.Id
LEFT JOIN "Tolls" t_to_from ON cp_from."ToId" = t_to_from.Id
LEFT JOIN "CalculatePrices" cp_to ON cp_to."ToId" = t.Id
LEFT JOIN "Tolls" t_from_to ON cp_to."FromId" = t_from_to.Id
LEFT JOIN "TollPrices" tp ON (tp."TollId" = t.Id OR tp."CalculatePriceId" IN (cp_from.Id, cp_to.Id))

WHERE 
    LOWER(t.Name) LIKE LOWER('%Toll Name%') 
    OR LOWER(t."Key") LIKE LOWER('%Toll Name%')
    OR LOWER(t."Number") LIKE LOWER('%Toll Name%')
    
ORDER BY t.Name, cp_from.Id, cp_to.Id, tp.Id;

-- Вариант 2: С JSON агрегацией (PostgreSQL) - более структурированный результат
SELECT 
    t.Id AS TollId,
    t.Name AS TollName,
    t."Key" AS TollKey,
    t."Number" AS TollNumber,
    t."StateCalculatorId",
    sc.Name AS StateCalculatorName,
    sc."StateCode",
    
    -- CalculatePrice где toll является From (в виде JSON массива)
    COALESCE(
        (
            SELECT json_agg(
                jsonb_build_object(
                    'CalculatePriceId', cp.Id,
                    'ToTollId', cp."ToId",
                    'ToTollName', t_to.Name,
                    'StateCalculatorId', cp."StateCalculatorId",
                    'Online', cp."Online",
                    'IPass', cp."IPass",
                    'Cash', cp."Cash",
                    'TollPrices', (
                        SELECT json_agg(
                            jsonb_build_object(
                                'Id', tp.Id,
                                'PaymentType', tp."PaymentType",
                                'Amount', tp."Amount",
                                'AxelType', tp."AxelType",
                                'Description', tp."Description"
                            )
                        )
                        FROM "TollPrices" tp
                        WHERE tp."CalculatePriceId" = cp.Id
                    )
                )
            )
            FROM "CalculatePrices" cp
            LEFT JOIN "Tolls" t_to ON cp."ToId" = t_to.Id
            WHERE cp."FromId" = t.Id
        ),
        '[]'::json
    ) AS CalculatePrices_AsFrom,
    
    -- CalculatePrice где toll является To (в виде JSON массива)
    COALESCE(
        (
            SELECT json_agg(
                jsonb_build_object(
                    'CalculatePriceId', cp.Id,
                    'FromTollId', cp."FromId",
                    'FromTollName', t_from.Name,
                    'StateCalculatorId', cp."StateCalculatorId",
                    'Online', cp."Online",
                    'IPass', cp."IPass",
                    'Cash', cp."Cash",
                    'TollPrices', (
                        SELECT json_agg(
                            jsonb_build_object(
                                'Id', tp.Id,
                                'PaymentType', tp."PaymentType",
                                'Amount', tp."Amount",
                                'AxelType', tp."AxelType",
                                'Description', tp."Description"
                            )
                        )
                        FROM "TollPrices" tp
                        WHERE tp."CalculatePriceId" = cp.Id
                    )
                )
            )
            FROM "CalculatePrices" cp
            LEFT JOIN "Tolls" t_from ON cp."FromId" = t_from.Id
            WHERE cp."ToId" = t.Id
        ),
        '[]'::json
    ) AS CalculatePrices_AsTo,
    
    -- TollPrice напрямую связанные с Toll (без CalculatePrice)
    COALESCE(
        (
            SELECT json_agg(
                jsonb_build_object(
                    'Id', tp.Id,
                    'PaymentType', tp."PaymentType",
                    'Amount', tp."Amount",
                    'AxelType', tp."AxelType",
                    'Description', tp."Description"
                )
            )
            FROM "TollPrices" tp
            WHERE tp."TollId" = t.Id AND tp."CalculatePriceId" IS NULL
        ),
        '[]'::json
    ) AS TollPrices_Direct

FROM "Tolls" t
LEFT JOIN "StateCalculators" sc ON t."StateCalculatorId" = sc.Id

WHERE 
    LOWER(t.Name) LIKE LOWER('%Toll Name%') 
    OR LOWER(t."Key") LIKE LOWER('%Toll Name%')
    OR LOWER(t."Number") LIKE LOWER('%Toll Name%');
