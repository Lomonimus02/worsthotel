# Verification record — Guest Presence / SOLO 0.2.2

Этот отчёт относится к [GUEST_PRESENCE_REQUEST.txt](../GUEST_PRESENCE_REQUEST.txt) и реализации [GUEST_PRESENCE.md](../GUEST_PRESENCE.md). Проверки предыдущей 0.2.1 сохранены в [history/VERIFICATION-0.2.1.md](../history/VERIFICATION-0.2.1.md): они остаются историей, а не результатом сборки 0.2.2.

**Подтверждены Scene/compile, финальный Windows build 0.2.2 и 216/216 EditMode, без failed/skipped.** Первый полный PlayMode завершился **35/36 passed, 1 failed, 0 skipped**; после исправления setup старого шумового теста целевой повтор прошёл **1/1**. Все 36 отдельных cases имеют успешный результат; свежий полный 36/36 не заявляется. Финальный SOLO-проход — **PASS / Errors=0 / ResetVerified=True**, 198,2 с. Два финальных EXE по localhost также **PASS / Errors=0**. Визуально приняты **12 текущих изображений: 9 SOLO из 26 и 3 LAN**; остальные 17 SOLO-кадров не заявлены просмотренными. [ACCEPTANCE_AUDIT.md](../ACCEPTANCE_AUDIT.md) сопоставляет доказательства с 20 критериями пользователя и сохраняет ручные границы.

## Текущие проверки

| Проверка | Подтверждённый результат | Артефакт |
| --- | --- | --- |
| Генерация сцены и компиляция 0.2.2 | PASS | [Scene log](../../Logs/p022-presence-scene.log) |
| Полный EditMode | **216/216 PASS**, failed 0, skipped 0; **3,8626861 с** | [EditMode XML](../../Logs/p022-presence-editmode.xml) |
| Первый полный PlayMode | **35/36 passed, 1 failed, 0 skipped**, **400,8703068 с**; новые 4 cases PASS | [PlayMode XML](../../Logs/p022-presence-playmode.xml), [log](../../Logs/p022-presence-playmode.log) |
| Целевой повтор шумового fixture после исправления | **1/1 PASS**, 0 skipped, **13,9076053 с**; это не новый полный прогон 36 cases | [Noise staging XML](../../Logs/p022-noise-staging-playmode.xml) |
| Финальный Windows build 0.2.2 | **PASS**; после первого build изменены только диагностические ракурсы/scroll | [Release build log](../../Logs/p022-presence-release-windows.log) |
| Финальные SOLO presence fixtures и отдельные три дня в EXE | **PASS / Errors=0 / ResetVerified=True**, **198,2 с**, 26 fresh nonblank GPU captures | [Runtime report](../screenshots/presence-solo/runtime-verification.txt), [manifest](../screenshots/presence-solo/capture-manifest.txt), [player log](../../Logs/p022-presence-player.log), [архив отчёта](../../Logs/p022-presence-player.txt) |
| Два финальных EXE по localhost с protocol 2 | **PASS / Errors=0** у host и client; **29 / 162** checks | [Host report](../verification/lan/20260926-120535-f938e247/host-report.txt), [client report](../verification/lan/20260926-120535-f938e247/client-report.txt) |
| Финальная SOLO-визуальная приёмка | Приняты **9 из 26** свежих кадров; остальные 17 не заявлены просмотренными | Точный список ниже |
| Финальная LAN-визуальная приёмка | Приняты все **3** свежих LAN-кадра | Точный список ниже |

## Модельные и физические проверки

EditMode проверяет, что душ не начинает таймер, шум и нагрузку на бойлер до физического подтверждения готовности; устаревший callback не меняет новую активность. Сон и душ дают private-состояние с отказом обычного входа. Временный выход сохраняет комнату и ключ, отличается от checkout и допускает возвращение в то же проживание. Checkout прекращает незавершённую активность; JSON snapshot protocol 2 сохраняет privacy/staging и отклоняет изменение read-only клиента. Источник этих проверок — [GuestPresenceTests.cs](../../Assets/_WorstHotel/Tests/EditMode/GuestPresenceTests.cs).

[Сценарии SOLO-механизма](../../Assets/_WorstHotel/Tests/EditMode/SoloRepairTests.cs) проверяют безопасное давление, настоящее непрерывное действие, конечное время защёлки, невозможность бесконечно продлевать её удержанием, отмену/reset и отсутствие этой помощи в multiplayer. Старые модельные регрессии входят в общий набор 216; отдельная физическая или сетевая проверка из этого не следует.

Все четыре новых PlayMode cases прошли в первом полном запуске:

- [GuestPresencePlayModeTests](../../Assets/_WorstHotel/Tests/PlayMode/GuestPresencePlayModeTests.cs), **33,851833 с**: настоящий путь к кровати, горизонтальная поза, пробуждение до ходьбы, закрытый душ с отложенной нагрузкой, временный выход/возвращение через двери; перенос позы и видимости в локальную replica. Последнее не является двухпроцессной сетевой проверкой.
- [RoomPrivacyPlayModeTests](../../Assets/_WorstHotel/Tests/PlayMode/RoomPrivacyPlayModeTests.cs), **7,124210 с**: реальный raycast/input у занятой двери, knock/permission, отказ private-гостя, отмена короткого emergency hold, завершение намеренного входа и выход изнутри.
- [SoloPlayModeTests](../../Assets/_WorstHotel/Tests/PlayMode/SoloPlayModeTests.cs): one-rig/WAIT/reset case **2,868635 с** — один фактический staff object, одна камера/AudioListener, отсутствие второго виртуального устройства и фиктивного голоса WAIT, сохранение режима при NewGame. Полный физический ремонт **32,330629 с** — настоящий клапан, ходьба и остальные органы управления. Это длительности cases, а не время обычного человеческого ремонта.

Единственный сбой — прежний `ClosedDoorKnockAndSeparateAskReduceRealNeighbourNoiseThenMeasuredComplaintRecovers`: ожидался quiet source 0,08, получен loud 0,75. Его fixture создавал жалобу синхронно до регистрации physical staging, измерял quiet во время ходьбы, а спустя 6 с гость реально доходил до радио. Root изменил только setup теста: дождаться регистрации presentation и `ActivityStaged` обоих гостей до создания жалобы. Целевой повтор **PASS 1/1**; production-акустика не менялась. Подпись NextScheduledActivity для временного сна уточнена; подсказка ремонта «keep relief supported» теперь подходит обоим режимам.

Диагностические setup-команды в этих tests явно отделены от проверяемого физического действия. В частности, принудительный сон/душ и поломка создают воспроизводимое условие, а не доказывают естественную частоту таких событий.

## Реализованное поведение

Каждый номер имеет BedAnchor, BedApproach, ShowerAnchor, RestAnchor, DeskAnchor и обе стороны дверного прохода. Подход и короткая постановка завершаются до запуска физической активности. Сон укладывает тело за **0,95 с**; пробуждение возвращает вертикальную позу за **0,85 с** перед ходьбой. Душ использует закрытый угол/штору, воду, пар и локальный звук; скрываются также стоящий interaction capsule и видимый ключ гостя. DeskAnchor является точкой окружения, а не заявлением о новой отдельной работе за столом.

Занятая дверь закрывается после прохода. Обычный вход требует стука и соответствующего ответа; private-сон/душ отказывают. Emergency entry — явное вооружение вторичным действием и **3 с** удержания основного действия, с отменой при прерывании. Временные сон и уход не завершают проживание. Ограниченное восстановление пути сохраняет сторону комнаты и не выдаёт выдуманное прибытие.

SOLO создаёт **одного настоящего сотрудника и одно назначение ввода**, без спящего actor1 или AI-напарника. WAIT требует одного действительного согласия. Для ремонта удержание настоящего клапана в зелёной полосе заряжает защёлку за **2 реальные секунды**; затем есть **18 активных реальных секунд** на прежний порядок panel → breaker → latch A → latch B → restart. Пауза, потеря устройства, истечение времени и новая сессия снимают защёлку. Времена находятся в `SoloAssist.asset`; отдельные множители экономики/терпения не добавлены. HOST сохраняет одновременную работу двух игроков. LAN использует **protocol 2**, host-only callbacks и read-only клиент.

## Финальный фактический SOLO EXE-проход

Команда `tools/VerifyPlayer.ps1 -Solo -PresenceFixtures -OutputPath docs/screenshots/presence-solo` завершила проход финального release EXE **PASS / Errors=0 / ResetVerified=True**. [Отчёт](../screenshots/presence-solo/runtime-verification.txt) фиксирует 0.2.2, Unity 6000.3.2f1, GTX 1650 SUPER, 1600×900 и **198,2 с** при диагностических часах 8×. В наличии **один staff object, одна активная камера, один AudioListener и один принадлежащий диагностике синтетический геймпад**; это повторно проверено после reset.

Отдельная disposable-сессия принудительно проверила Sleep/Shower/Rest/Leave/Return, затем была сброшена перед трёхдневным проходом. Presence fixture PASS: horizontalUpDot=0, ShowerConcealed=True, RealShowerDemand=True, LeaveReturn=True; callbacks маршрутов — производственные. В трёх днях booked/checkedIn/actualRoomArrivals/paidStays составили **4/6/6**, получены **3 отчёта**, **1 физическое переселение** и **11 MODEL BedActions / 11 MODEL LinenTurnovers**. Финальные деньги **$2379**, репутация **52,41743**.

Финальный [manifest](../screenshots/presence-solo/capture-manifest.txt) содержит **26 свежих непустых GPU-кадров**. Root открыл и принял ровно **9**:

| Финальный кадр | Подтверждённый визуальный результат |
| --- | --- |
| [guest-sleep](../screenshots/presence-solo/guest-sleep.png) | Горизонтальная поза на кровати |
| [guest-shower](../screenshots/presence-solo/guest-shower.png) | Закрытый угол с бирюзовой шторой и паром; тело скрыто |
| [guest-privacy](../screenshots/presence-solo/guest-privacy.png) | Подход сотрудника к закрытой занятой комнате |
| [guest-rest](../screenshots/presence-solo/guest-rest.png) | Видимый гость восстановлен у точки отдыха |
| [guest-debug](../screenshots/presence-solo/guest-debug.png) | Все семь требуемых полей состояния и force controls читаются |
| [solo-service](../screenshots/presence-solo/solo-service.png) | Одна полноэкранная игровая камера SOLO |
| [planning](../screenshots/presence-solo/planning.png) | Планирование первого дня |
| [results](../screenshots/presence-solo/results.png) | Итоги трёх дней |
| [new-session](../screenshots/presence-solo/new-session.png) | Свежая SOLO-сессия после reset |

Остальные **17** кадров не заявлены визуально просмотренными. Первый проход также был PASS (198,6 с), но shower-ракурс заслоняла кровать, а debug-scroll обрезал нужные поля. Эти отклонённые кадры сохранены в [presence-first-rejected](../screenshots/presence-first-rejected/), первый [отчёт](../../Logs/p022-presence-player-first.txt) и [лог](../../Logs/p022-presence-player-first.log) — отдельно. После изменения только диагностической камеры/scroll собран release и повторён весь проход. Финальные shower/debug выше просмотрены и приняты; первоначальные недостатки не выдаются за непройденную текущую проверку.

В трёхдневном tour работают производственный update loop, реальные маршруты гостей и двери. Действия ключей и ручного белья выполняются обозначенными **MODEL-командами**; это не физическая доставка каждого комплекта игроком. Ускоренные диагностические часы не являются человеческим WAIT, оценкой удобства, длительностью обычной сессии или FPS benchmark. Физические действия проверяются отдельными PlayMode cases. Принудительные presence fixtures нельзя выдавать за естественные события последующего трёхдневного прохода.

## Финальный localhost LAN: два настоящих EXE

Запуск **`20260926-120535-f938e247`** финальной сборки прошёл **PASS / Errors=0** на обеих сторонах: [host — 29 checks](../verification/lan/20260926-120535-f938e247/host-report.txt), [client — 162 checks](../verification/lan/20260926-120535-f938e247/client-report.txt). Использованы обычные HOST/JOIN и реальный NGO transport, без диагностического RPC. Служебные файлы согласуют ожидания и сравнивают измерения, но не применяют состояние отеля.

| Наблюдение | Подтверждённый результат |
| --- | --- |
| Камера / AudioListener / authority | По одному Camera и AudioListener на процесс; host actor0 имеет authority, client actor1 — read-only world |
| Клиентские команды | **2**: Assign и Commit прошли через host и вернулись снимками |
| Физический ключ | Реальный client input: pickup → carry **2,38 м** → drop → отход **0,68 м** до горизонтального расстояния **1,79 м** → regrab → release при disconnect |
| Поза сотрудника и ключа | **WorldPoseAgreement=True**, обе ошибки **0,000 м** |
| Часы и модель | **HostClockAdvanced=True**, **609** проверок client clock только по snapshot, локальный Tick отклонён |
| Disconnect | Перед намеренным выходом клиент остался read-only, его физика отключена; хост освободил переносимый предмет |
| Возврат и SOLO | После LeaveToMenu создана свежая локальная сессия; production StartSolo подтвердил **SoloAfterLan=True, StaffObjects=1** |
| Snapshot sizes в этом состоянии | Model **10 948 B**, world **104 277 B**; это не benchmark пропускной способности |

Пустой удалённый actor1 диагностически размещён возле ключницы; дальнейшие предметные действия выполнялись реальным синтетическим вводом клиента через транспорт. Only-owned input не отправлялся в физические устройства. Этот smoke проверяет минимальную совместимость текущего LAN, но не полный сетевой путь передачи ключа гостю, ручной смены белья, совместного ремонта, нескольких дней или reconnect. Staged guest pose/visibility отдельно проверены локальной PlayMode-replica; данный двухпроцессный smoke не выдаётся за отдельный сетевой цикл сна/душа.

Root открыл и принял все **3 свежих LAN-кадра**: [client-one-camera-play](../verification/lan/20260926-120535-f938e247/client-one-camera-play.png), [client-connection-menu](../verification/lan/20260926-120535-f938e247/client-connection-menu.png), [client-host-join-menu](../verification/lan/20260926-120535-f938e247/client-host-join-menu.png). Меню SOLO/HOST/JOIN и игровое состояние используют одну камеру. Последний кадр относится к свежей локальной сессии после намеренного выхода, а не к отключённому зеркалу. Вместе с девятью SOLO-кадрами выше приняты **12 текущих изображений**.

## Ручные границы

Остаются человеческая оценка правдоподобности приватных действий и переходов, удобства knock/emergency access, локализации и уровня звука, переноски/белья, напряжённости 18-секундного SOLO-окна и темпа трёх дней. Также отдельно нужны два физических компьютера, реальные контроллеры и наблюдение производительности на обычном экране. Ни автоматический gate, ни скрытое окно, ни отдельные GPU-кадры не заменяют эти проверки.

Команды и инструменты воспроизведения: `tools/PhaseGate.ps1`, `tools/Unity.ps1`, `tools/VerifyPlayer.ps1`, `tools/VerifyLAN.ps1`. Каждый новый результат должен ссылаться на собственные logs/XML и свежий manifest. Управление и сетевые ограничения описаны в [HOW_TO_PLAY.ru.md](../HOW_TO_PLAY.ru.md) и [NETWORKING.md](../NETWORKING.md).

