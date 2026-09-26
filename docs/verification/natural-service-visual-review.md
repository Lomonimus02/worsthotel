# Prototype 0.3.2 — визуальный осмотр SOLO

26 сентября 2026 года. Главный агент, выполнявший проверку сборки, сообщил о просмотре **8 из 38** реальных PNG завершённого SOLO-запуска. Этот журнал фиксирует именно переданные наблюдения; остальные 30 кадров не включены в осмотр. [Отчёт запуска](../../Logs/natural-service-player.txt), [manifest](../screenshots/natural-service-solo/capture-manifest.txt).

Игровая DLL проверенного запуска: `2072C60A38E910873ACFA91508F3BC31AB189F08651153E07DB6EFB6931A35E3`. SHA256 перечисленных PNG повторно прочитаны с диска при записи журнала.

| Кадр | Подтверждённый объём просмотра |
| --- | --- |
| [service-board](../screenshots/natural-service-solo/service-board.png) | Просмотрена доска услуг. Это отдельный регрессионный отель с прежним общением; кадр не доказывает новую приватность сведений. |
| [natural-self-help](../screenshots/natural-service-solo/natural-self-help.png) | Видно уже полученное состояние после самопомощи. **Сам жест поворота клапана на кадре не подтверждён**; маршрут и реальная мутация проверены отчётом и PlayMode. |
| [natural-phone-ringing](../screenshots/natural-service-solo/natural-phone-ringing.png) | Общий входящий звонок до раскрытия номера/причины; интерфейс читаемый, без замеченного наложения. |
| [natural-phone-heard](../screenshots/natural-service-solo/natural-phone-heard.png) | После ответа раскрыты сведения и варианты ответа; интерфейс читаемый, без замеченного наложения. |
| [natural-reception-waiting](../screenshots/natural-service-solo/natural-reception-waiting.png) | Общая подсказка ожидающего у стойки; вид читаемый, без замеченного наложения. Один кадр не доказывает весь путь из номера. |
| [natural-reception-heard](../screenshots/natural-service-solo/natural-reception-heard.png) | Разговор у стойки после раскрытия; варианты читаемые, без замеченного наложения. Возврат проверен состоянием/маршрутом в отчёте. |
| [guest-detail](../screenshots/natural-service-solo/guest-detail.png) | Просмотрена страница выбранного гостя в длинном трёхдневном проходе. |
| [electrical-tripped](../screenshots/natural-service-solo/electrical-tripped.png) | Просмотрен кадр отключения линии. Причина 4,55/4 и независимость A подтверждены отдельно игровым отчётом, не выводятся только из изображения. |

| PNG | SHA256 |
| --- | --- |
| service-board.png | `3A59EA5C16C4CB9EA272B04652D6D93F45B126C15DFE7EE5B66A87FBF29E94B0` |
| natural-self-help.png | `543DD22C98B8DA7B6B7FE77B1DAE0AA5E760C277A40B256DC61D31AB22F64F72` |
| natural-phone-ringing.png | `13A0A5C6951A935BD4FB9D5435460F807DB00496FEF90CF056C2307AD8EE7F99` |
| natural-phone-heard.png | `C22B04AC6D71BD59D074F21904AF0719FCBBC6B33C3FB6A7314015BF7DF8C861` |
| natural-reception-waiting.png | `C6FC4195952593BE488DC82974360EB362726F57343672B2221240939754E09D` |
| natural-reception-heard.png | `D08ED51DDC18DC9D228C9635FC129DD7DBFE77D65CD689049A5699417C334045` |
| guest-detail.png | `55FAB65937C80C54112734F6F7F26DC1F6E869DA8FBEFC6BCEEF48F52DC617D7` |
| electrical-tripped.png | `42AD2750DE246FD2638C10E5D673F3A8DD98BD1C4F1796484E2C131E99DB70CA` |

Статический просмотр не подтверждает слышимость звонка, плавность жеста, удобство управления, FPS или Alt+Tab. Результаты и новая сессия пока не просматривались в рамках этих восьми кадров; их числовые проверки находятся в отчёте запуска. LAN-кадры относятся к отдельной проверке и сюда не включены.
