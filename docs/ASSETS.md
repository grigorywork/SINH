# Источники ресурсов

Единственный значок — голубой зеркальный SINH (`mirror.png` / `mirror.ico`). По последнему заданию владельца прежние розовый, матовый и лист удалены вместе с выбором. ICO содержит 16/24/32/48/64/128/256 px, получен конвертацией Pillow из утверждённого PNG. Значок не меняет подписанный EXE или identity.

MaterialDesignInXamlToolkit: https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit, MIT. Версия NuGet 5.4.0. Лицензии зависимостей включаются в portable-пакет.

`design/sinh-mirror-concept.png`: эскиз собственного значка SINH, получен image_gen 2026-11-24 по референсу владельца и уточнению «элементы должны быть зеркальны». Прозрачный PNG. Утверждён владельцем как основа значка; не является точным векторным мастер-файлом.

Десять шрифтов (regular/bold) взяты без изменений из того же закреплённого NoteLS-исходника `app/src/main/res/font`. Лицензии из `app/src/main/assets/font-licenses` сохранены рядом и включаются в portable-пакет. URL и SHA256 каждого файла записаны в `src/Sinh.App/Assets/Fonts/manifest.json`. Семейства встроены как WPF resources, FontFamily создаётся/кешируется только по выбору.

По последнему референсу владельца зеркальный голубой концепт включён как mirror.png/mirror.ico и выбран по умолчанию для нового профиля. PNG копируется без творческих изменений; ICO получен конвертацией Pillow в 16/24/32/48/64/128/256 px. Прежние варианты удалены.

Лицензии MaterialDesignThemes/Colors получены из закреплённого commit 14223ece627401f4e987fdd4ba2b1ac5674977ff, XamlBehaviors — eaf05bcb7395e3fbbb930f863180defc8e9928f7. Уведомления и лицензии .NET/WindowsDesktop взяты из runtime NuGet 10.0.15. Тексты в Assets/Licenses встроены в EXE и приложены к пакету.

Также сохранены Noto OFL из того же pinned toolkit commit и отдельные Apache 2.0/Google notices для Roboto, встроенного в MaterialDesignThemes (метаданные TTF). Это отдельная поставка от Roboto NoteLS под OFL. Инспекция ресурсов подтверждает 20 TTF SINH и 18 TTF MaterialDesignThemes; Все десять семейств проверены на Windows Server 2025: кириллица и латиница присутствуют.

Для Windows WPF Merriweather заменён на немодифицированные upstream static v2.002 regular/bold/OFL: google/fonts commit b848f42f0bc0a8bc53588c3688ca37890e1db0bd, ofl/merriweather. Исходные NoteLS-файлы с несовместимыми WPF метаданными не использованы в окончательном preview. Reserved Font Name не менялся; новые SHA/URL зафиксированы в manifest.
