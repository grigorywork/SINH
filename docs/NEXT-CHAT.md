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

## Последнее уточнение владельца — публикация публичная

Владелец явно указал «также в открытом доступе»: создавать SINH как public, исходник и сборку размещать публично. Повторная попытка `gh repo create grigorywork/SINH --public` также отклонена: Resource not accessible by integration (createRepository). Нужен созданный владельцем публичный репозиторий и доступ интеграции. Код/артефакты остаются локально; публикация не состоялась. Не публиковать приватный ключ.

## Доступ к GitHub предоставлен
Владелец создал grigorywork/SINH, public; API подтвердил push/admin. Удалённый первоначальный README объединён с локальным исходником с сохранением обеих Git-историй. Далее push main, Windows CI и публичный preview Release с проверенными артефактами.

## Первый Windows CI
Исходники успешно опубликованы в public grigorywork/SINH, main36e0076. Run45687215216 на Windows Server2025/10.0.26100 собрал все проекты без warning, но ядро60/61: восстановление пути>500 упало в P/Invoke CreateFileW без extended prefix. Исправлено добавлением \\?\ / \\?\UNC\ до native CreateFile; существующая регрессия проверяет реальный случай. Кандидат повышен до0.4.0-preview.2, старыйv1 остаётся draft. Нужен повтор Windows CI и новый подписанный portable.

Windows run45687751429: **61/61 ядра PASS**, включая длинные Unicode пути и restore. WPF harness упал до окон из-за запрещённой смены Application.ResourceAssembly при существующей entry assembly. Убрано присваивание; URI ресурсов App теперь явно SINH;component через AssetUris, чтобы работать и из тест-хоста, и из EXE.

Windows run45688434617: engine61/61; WPF достиг настоящего MainWindow и выявил TwoWay по умолчанию у ProgressBar.Value для read-only ProgressValue. Явно задан Mode=OneWay. Следующий прогон обязателен перед публичной сборкой.

Windows run45688690683: engine61/61, WPF MainWindow открылся; найден пропавший icon, потому что implicit Style TargetType=Window не наследуется производными Window-классами. WindowLoaded теперь явно присоединяет общий window style, сохраняя локально выбранный style.

Windows run45689082987: MainWindow/theme/icons/lazy sections/GC/scales/glass draft/English/small window/cancel-dialog PASS. Выявлено WPF имя семейства Merriweather: pinned static TTF содержит legacy name ID1 «Merriweather Light», weight400/700. URI ссылается на legacy имя, UI-имя сохраняется Merriweather; файлы и OFL Reserved Font Name не менялись.

Пользователю выдан отдельный самоподписанный EXE0.4.0-preview.2 (SHAa5f9453c5616b73feae4f8b6aa4de5b07e3bf621094124361c5f7524b26f17f6), с честно обозначенным дефектом дополнительного Merriweather. Публичный финальный preview повышен до.3: Merriweather заменён на оригинальные static2.002 Google Fonts b848f42f0bc0a8bc53588c3688ca37890e1db0bd, с исходной OFL и обновлённым manifest; другие9семейств не менялись. Новый Windows CI обязателен.
