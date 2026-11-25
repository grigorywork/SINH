# SINH — текущая передача

Версия **0.4.0-preview.1**, `/workspace/sinh`, main. SDK 10.0.404, .NET 10 LTS, WPF/MVVM, MaterialDesignThemes 5.4.0. Источник ядра: **705f600** (полный SHA в artifacts/qa-optimized-hour/SOURCE-COMMIT). UI после этого имеет ещё незакоммиченные улучшения (читаемые пути и старт в трее без показа окна).

## Цель и ограничения

Пользователь просит автономно закончить этапы SINH, рефакторинг, аудит/исправления/оптимизацию по часу, затем анимации, portable, цифровую подпись и GitHub. Не задавать вопросов, не покупать сервисы/платные ИИ. Windows 10/11 x64 — сначала; Linux GUI позже. Не использовать subagents: текущие developer instructions запрещают без явного задания. Rust-скрин — пример организации модулей, не смена C#/WPF.

Важно: Unicode/длинные русские и английские пути; посторонние файлы USB вне пар не трогать; B — выделенный подкаталог. USB-изменения требуют выбора направления/плана. MSS9LN: каждое второе нажатие — подпрыгивание, девятое — пасхалка.

## Реализовано

- Core Discovery → Merge strategies → OperationPlanner → Executor; Settings отдельная библиотека; WPF Views/ViewModels/Services/Platform/Windows. См. ARCHITECTURE.md.
- Updates/Mirror/TwoWay, подтверждённая база, конфликты с обеими версиями, явные удаления/массовые планы, ссылки/исключения, SHA256, проверяемые temp/atomic commit, отмена, история и recovery с ID томов.
- PayloadSources передаёт неизменяемые корни напрямую; сохраняет payload изменяемых корней до замен (включая B-extras case rename). Нет обязательной второй полной копии обычного backup на ПК.
- StableRoot GUID/UNC для I/O, человекочитаемые пути отдельно; Volume ID обеих сторон, защита от чужой буквы диска; FAT/name limits. Native API ещё не проверено на Windows.
- Наблюдение/debounce/контрольная сверка/интервалы/USB, ограниченная очередь, пауза/стоп, трей/Exit, один процесс, HKCU Run/helper (по умолчанию выключен).
- SettingsStore atomic/immutable/no-op/backup recovery/flush. Экспорт/импорт (новые ID, пауза, автоматика/удаления off), отдельные копии конфигурации 0/1/3/10/30 дней.
- Темы/плашки/масштаб/системный акцент/четыре значка (голубой mirror + три сохранённых), 10 offline шрифтов с лицензиями/manifest, glass draft/preview/fallback, RU/EN, виртуализация и прокрутка маленького окна/DPI hook.
- A-001..A-011 в AUDIT.md. Последние фиксы: атомарный create без overwrite неожиданного нового файла и reserved GUID temp namespace с видимым skip; пользовательские .tmp не скрываются.

## Проверки и текущие процессы

**55/55 PASS Linux x64** на последней frozen-сборке; WPF cross-build без warnings/errors. Это НЕ Windows runtime. PowerShell/WindowsChecks подготовлены, но ни разу не запускались на Windows. Реальные Win10/11, USB/SMB, NTFS/exFAT/FAT32, Narrator, DPI/мониторы/portable запуск — pending.

- Предварительно: 30 с/597 случаев/0 обнаружений, 2026-11-24.
- Audit hour: 05:19:57–06:19:57 2026-11-25, 3600.036 с, 72 506 случаев, **1497 ошибок старого ядра**. Отчёт audit-hour.json, не называть успешным.
- Fix run 06:22 прерван средой на 3154.147 с: 63 659 случаев, 0 ошибок. Отчёт fixes-interrupted.json, не считать полным часом.
- **Fix hour завершён:** 10:48:52.578–11:48:52.617, 3600.038 с, **62 921 случай**, 9751 отмена, 14 633 guard refusals, **0 ошибок**. Отчёт fixes-restart-hour.json. Старый PID4500 завершён.
- **Optimization hour сейчас выполняется:** начало **11:48:59.849 UTC**. Не ранее **12:48:59.849**, плюс минимум **73 000 случаев** (может длиться дольше). PID **8045**, координатор PID **6088**, unified exec session **86461**. Отчёт docs/validation/2026-11-25-optimization-hour.json, логи artifacts/optimization-hour.log. Координатор затем запустит benchmark в performance-final.json.
- Frozen DLL artifacts/qa-optimized-hour **НЕ ПЕРЕЗАПИСЫВАТЬ**. Core SHA256 **8C9C2CA48E42D5FAB557DA64F8A1861BA3CF106230C84B6B9A78B81EA522C44F**. Тот же Core сейчас в src/Sinh.Core/bin/Release/net10.0. Метаданные sourceCommit705f600. Не пересобирать библиотеку ради UI/документов; App build с `-p:BuildProjectReferences=false` и проверкой SHA сохраняет ровно тестируемое ядро для пакета.
- Сохранять фактическое время; не выдавать ожидание/перерыв за час работы. Пользователь спросил ETA в 11:35: ответ — около 1.5 часа до локального preview. После fix hour уточнено 70–80 минут.

## Далее

1. Проверять report/checkpoint и дождаться успешного optimization hour + min cases; не останавливать. При новой steering input ответить кратко и продолжать, если не отменяет цель.
2. Проверить результаты benchmark/память; обновить AUDIT/WORK-LOG и Git. GitHub creation blocked, не повторять бесконечно.
3. **Последними** добавить умеренные UI fade/expander анимации и улучшить динамический reduced-motion в пасхалке. Existing bounce/egg уже есть. App-only build `-p:BuildProjectReferences=false`, Core hash должен остаться 8c9c… . Windows UI всё ещё pending.
4. Финальный publish `dotnet publish src/Sinh.App/Sinh.App.csproj -c Release -r win-x64 --self-contained true --no-build -o artifacts/publish`. Первый candidate успешно опубликован ~185 MiB, но устарел относительно последних Core фиксов — обязательно перепубликовать. Внешних DLL нет; PDB не включать в portable. Assets лицензии прилагаются.
5. Подписать FINAL EXE самоподписанным MSS9LN, проверить с явным публичным CAfile (не менять system trust), ZIP/readback/SHA256, source ZIP + Git bundle, release metadata с точными проверками и ограничениями. Проверить, что байты frozen Core присутствуют в single-file EXE (compression не включена).
6. Сохранить всё в Git, docs/NEXT-CHAT.md, выдать локальные ссылки. Не объявлять Windows 10/11 проверенными и GitHub опубликованным.

## Подпись и GitHub

GitHub user grigorywork; SINH GET404. `gh repo create grigorywork/SINH --private ...` отклонён: **GraphQL Resource not accessible by integration (createRepository)**. Origin не появился. Не обходить отказ другим UI/учёткой, не писать в NoteLS. Пользователь явно просил публикацию; локальные commits разрешены. Trusted Authenticode certificate не предоставлен.

Для preview создан **самоподписанный, не доверенный** leaf code-signing сертификат MSS9LN, RSA3072/SHA256. **Приватный ключ только вне Git:** `/workspace/private-sinh-signing/mss9ln-preview.key`, mode600, folder700. PEM cert там же mss9ln-preview.pem, DER MSS9LN-preview.cer. Никогда не включать/печатать/выдавать ключ. Public fingerprint: E3:01:D5:4D:58:E8:78:11:24:05:E3:AF:03:14:92:A9:51:57:1A:B7:FA:63:E4:73:08:8F:18:EB:E8:9E:58:3A, expires 2027-05-24. Ни один FINAL EXE пока не подписан. Tool `/workspace/.tools/osslsigncode/usr/bin/osslsigncode` 2.9, OpenSSL available. Не устанавливать сертификат в доверенные, не покупать trusted cert.

## Полезные команды

SDK `/workspace/.dotnet/dotnet`. App-only build `build src/Sinh.App/Sinh.App.csproj -c Release --nologo -warnaserror -m:1 -nr:false -p:BuildProjectReferences=false`. Frozen tests `dotnet artifacts/qa-optimized-hour/Sinh.Checks.dll`. `scripts/Check-Windows.ps1` и workflow сохраняют WPF PNG/логи на Windows; не запускались.

Font manifest проверен: 31 файл, 4 072 676 bytes, все SHA совпадают. Лицензии runtime/toolkit/fonts embedded + sidecars. Отдельные файлы в Assets/Licenses; проверить лицензии Noto/Roboto, которые также могут быть встроены в MaterialDesignThemes. Toolkit tree paths сохраняются в artifacts/toolkit-licenses.json.

## Доработки оболочки во время заключительного прогона
App-only: отмена/Exit закрывает modal план/выбор направления через CancelableDialog (иначе ожидание могло держать процесс открытым). Флаг редактирования пары держится до окончания async validation/save, команды не стартуют старый профиль в промежутке; Coordinator берёт актуальную конфигурацию из Store. Сохраняется текущая пауза от трея. Установка/relaunch помощника запрещена во время активного запуска. Добавлена Windows-регрессия cancel-dialog (пока только сборка).

## Критическое обновление A-012/A-013 — FINAL CORE ДРУГОЙ
Во время заключительного часа найдена отдельной регрессией потеря B-extra при case-only rename + History=false + cancel: 55/56 до фикса. OperationHistory создаёт обязательные safety-копии источников, которые ещё нужны последующим copy-шагам; они остаются в истории после отмены и очищаются после успешного commit. Проверены восстановление/повтор/полный лимит. Также case alias мог обойти запрет file-directory replacement (57/58 до фикса); тип проверяется заранее, изменения требуют конкретного review. **58/58 после фиксов**.

Старую frozen qa-optimized-hour (8c9c… / source705f600) НЕ ТРОГАТЬ: час всё ещё идёт и профилирует прежний снимок. Финальная библиотека теперь новая, будет frozen отдельно в artifacts/qa-safety-final после этого commit. Нужен отдельный целевой прогон --soak 300 2000000 <safety-stress.json> 5000 --no-history. Генератор добавляет case-only rename, отключает обычную историю и проверяет сохранность всех payload будущих операций после отмены, включая SHA архивов. Это дополняет, а не подменяет старый час. В финальном отчёте различать два SHA.

App-only новые фиксы: сохранение свежего snapshot конфигурации при старте, cancel закрывает окна подтверждения, busy editor до async save, helper install не во время copy, watcher накапливает сигнал во время busy/dialog (затем сверка), корни не Trim-ятся. UI всё ещё Windows-unverified.

## Фактическое возобновление 19:07 UTC
После перезапуска среды процессы отсутствовали. Fix hour завершён (3600.038 с/62921/0). Optimization достиг 3605.204 с/69111/0, но был прерван до внутренней цели73000: Completed=false сохраняется честно. Запущен supplement со следующего seed1069111, 3889 случаев; пауза 12:49–19:08 не включается в активное время. Финальное ядро0845f11 frozen в artifacts/qa-safety-final, SHA8c299baa497cf6993186bda66ddf2f8effd14b9087f7cdafe8a82527cdb473f8, 58/58 PASS. Запущен отдельный safety stress300с/min5000/seed2000000/--no-history. Анимации добавлены последним UI-этапом и cross-build успешен; ещё не проверены на Windows. Дождаться двух коротких прогонов, benchmark, финального publish/sign/package.
