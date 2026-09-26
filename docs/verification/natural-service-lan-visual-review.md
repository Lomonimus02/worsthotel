# Prototype 0.3.2 — визуальный осмотр LAN услуг

Успешный запуск **20260926-195142-64261dec**, два настоящих EXE по localhost: [host](natural-service-lan/20260926-195142-64261dec/host-report.txt) — 84 checks, [client](natural-service-lan/20260926-195142-64261dec/client-report.txt) — 446 checks, Errors=0 у обоих. Главный агент, выполнявший запуск, просмотрел **все 7 PNG** и сообщил о читаемых контролах без замеченного обрезания/наложения.

Финальная игровая DLL: `2BB387AA6CD2BBCAAA4C0033CC92C3D84C07B7E02047FEBB22E755354659BC63`. Хеш DLL и SHA256 PNG повторно прочитаны с диска при составлении журнала; это не новый независимый просмотр изображений.

| Кадр | Наблюдение |
| --- | --- |
| [client-natural-cold-ringing](natural-service-lan/20260926-195142-64261dec/client-natural-cold-ringing.png) | Общий входящий звонок до ответа, без раскрытия холодного случая. |
| [client-natural-cold-heard](natural-service-lan/20260926-195142-64261dec/client-natural-cold-heard.png) | После ответа показаны номер 106, проблема холода и отдельные варианты ответа. |
| [client-natural-wake-ringing](natural-service-lan/20260926-195142-64261dec/client-natural-wake-ringing.png) | Второй общий входящий звонок до раскрытия просьбы о пробуждении. |
| [client-natural-wake-heard](natural-service-lan/20260926-195142-64261dec/client-natural-wake-heard.png) | Номер 106 и просьба о звонке раскрыты после ответа; варианты доступны отдельно. |
| [client-service-stock](natural-service-lan/20260926-195142-64261dec/client-service-stock.png) | Исходный запас трёх одеял. |
| [client-service-delivered](natural-service-lan/20260926-195142-64261dec/client-service-delivered.png) | Доставленное дополнительное одеяло видно на кровати. Один PNG не доказывает сам перенос. |
| [client-service-phone](natural-service-lan/20260926-195142-64261dec/client-service-phone.png) | Финальный телефон: обещание выполнено вовремя, ожидающих звонков нет. |

| PNG | SHA256 |
| --- | --- |
| client-natural-cold-ringing.png | `9F8899C5F4A5E0F8E8687A3C16FFD3527F77C9717C029CA6B7DBAD95D7269AA9` |
| client-natural-cold-heard.png | `C5FB3C01FB558B5B2A0868F610833E2824F882180CAECB20550A8959C0E267A7` |
| client-natural-wake-ringing.png | `B45F722A116D9A4F7BD5DE20792261CD80EE8DA77ED6B272EDBAB4BC740AAFAC` |
| client-natural-wake-heard.png | `E15349A91C5F0860FBCE50689458008F2A1C66AAF14B84330BC5701989C92FBE` |
| client-service-stock.png | `F085F7DFD5484017C6281901F0080133B1FCBDB154582AF304DD22966A6FDCAD` |
| client-service-delivered.png | `30B9707795A94F90E444BA6472F993E14403FA7E96196CE4554F38B4585B4B20` |
| client-service-phone.png | `96B818D0512D13EBF6D491E699478C3B76CCF603E0CB5CADEC0E5A21B5781483` |

Гостевые маршруты, фактическое владение, перенос и репликация доказываются игровыми проверками отчётов, а не статическими кадрами. В [журнале клиента](natural-service-lan/20260926-195142-64261dec/client-player.log) есть одно `Receive queue is full` предупреждение; оно не объявлено исправленным. Errors=0 и успешный сценарий не доказывают отсутствие потерь пакетов, задержек или проблем производительности. Слышимость звонка, удобство двух людей, два физических компьютера и Alt+Tab этим осмотром не проверяются.

Три PNG отдельного основного LAN-прогона `20260926-195504-d5f1c7cc` не просмотрены и в этот визуальный результат не включены.
