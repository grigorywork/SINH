# SINH — итог текущего этапа

Версия **0.4.0-preview.1**, main, `/workspace/sinh`. SDK10.0.404, .NET10 LTS, WPF/MVVM, MaterialDesignThemes5.4.0. Финальный код оболочки278705c, ядра0845f11. Локальный preview упаковывается в `artifacts/release/`; подробности `RELEASE-0.4.0-preview.1.md`.

## Сделано

Модульный Discovery/Planning/Execution/Persistence, отдельные Settings/App/Checks. Три режима; план без записи, SHA256/streaming/temp/atomic replace, защита границ, GUID/UNC, ссылки/исключения/служебные temp, конфликты, история/restore/recovery, mandatory safety при отмене без обычной истории. Настройки atomic/immutable/flush, импорт с выключенной автоматикой, отдельные backup. Windows helper/USB-ID/scheduler/watch/tray/Exit; обычные права, автозапуск off по умолчанию. Темы/Material Design, RU/EN, масштаб/DPI hook/маленькое окно, offline-шрифты с лицензиями, стекло-черновик, mirror+три сохранённых значка. MSS9LN: 2-е нажатия прыгают, 9-е — пасхалка. Последними добавлены короткие появления окон/разделов и динамический reduced-motion.

## Проверено

58/58 Linux x64; App и WindowsChecks cross-build 0 warnings/errors. Полный час исправлений62921/0. Оптимизация старого снимка705f600:3605с/69111/0 + после паузы3889/0, всего73000 seeds. Оригинальный Completed=false сохранён; пауза не считается работой. Финальное ядро0845f11 проверено отдельно:328с/5000/0 без обычной истории. Отчёты: docs/validation/2026-11-25-summary.json и исходные JSON. Аудит исходной версии72506/1497 обнаружений не считать PASS.

Финальный Core SHA8c299baa497cf6993186bda66ddf2f8effd14b9087f7cdafe8a82527cdb473f8 сохранён в artifacts/qa-safety-final и побайтово включён в single-file EXE. App-only сборка с BuildProjectReferences=false сохраняет этот binary. Проверены31 font asset SHA /4072676bytes.

## Ограничения и следующий шаг

Windows runtime **не запускался**: ни Win10, ни Win11, ни физические USB/SMB/DPI/Narrator/portable launch не подтверждены. Следующий этап — `scripts/Check-Windows.ps1` и матрица docs/WINDOWS-VALIDATION.md на Windows, затем исправления реальных находок. Linux GUI — после Windows. Не выдавать cross-build за рабочий Windows-релиз.

GitHub creation заблокирована правами интеграции (`createRepository`, свежий GET404). Origin не появился; публикация не выполнена. При появлении разрешённого репозитория проверить доступ и выгрузить main/portable, не менять видимость и не использовать NoteLS. Пользователь уже разрешил публикацию; дополнительных вопросов о рутине не нужно.

Подпись preview **самоподписанная MSS9LN**, не доверенный издатель. Authenticode целостность проверена явным публичным certificate; system trust не менялось. Закрытый ключ вне Git и вне пакета. EXE SHA b6a9adc373b3e5c5ec5079d4c76f4ac7a1b42b33e05c6f4107fdc19a712bdc86. Финальные ZIP SHA в artifacts/release/SHA256SUMS.txt.

Пользователь просит работать автономно, ничего не спрашивать, сохранять этапы в Git и рефакторить по модулям. Не покупать сервисы/платные ИИ. Не использовать subagents без явного задания. Личные данные не трогались: проверки только на временных каталогах.
