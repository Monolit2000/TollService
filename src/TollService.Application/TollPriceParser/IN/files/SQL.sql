SELECT * from public."Tolls"
where "StateCalculatorId" = '28de45dd-8f08-46c4-a025-a9e749a6c2f2'

UPDATE public."Tolls"
Set "PaymentMethod_App" = FALSE, "PaymentMethod_Cash" = TRUE, "PaymentMethod_NoCard" = FALSE, "PaymentMethod_Tag" = true, "PaymentMethod_NoPlate" = TRUE,
"WebsiteUrl" = ''
where "StateCalculatorId" = '28de45dd-8f08-46c4-a025-a9e749a6c2f2'