# VORON — Character Prototype

Первый технический milestone психологического detective horror от первого лица.
**Unity 6000.4.4f1 · Built-in Render Pipeline · Input System 1.19.0 · Unity UI 2.0.0.**

## Текущее состояние

В репозитории находятся исходный Unity-проект, оригинальные rigged low-poly модели
Алексея Ворона, Елены Ворон, доктора Ильи Морозова и полицейского, текстуры,
модульные игровые системы и Editor-сборщик прототипа.
У каждого персонажа есть собственные анимации Idle и Talk (жесты, дыхание, перенос
веса), смена текстур рта при речи и моргание; превью — в [Art Pipeline](Docs/ArtPipeline.md).

**Первый игровой milestone пока не закрыт.** В sandbox установлен нужный Unity,
но его запуск остановлен отсутствием активированной лицензии. Нативный импорт,
генерация `.prefab`/`.anim`/`.asset`/`.unity`, Unity Console и Play Mode здесь
не подтверждены. Эти файлы создаются Editor-сборщиком после первого успешного
импорта; не следует путать исходники сборщика с уже проверенной игровой сценой.
Unity/Blender MCP в этой сессии не предоставлены; использован Blender CLI.

Изображения в `Docs/Media` — **рендеры Blender, не скриншоты Unity**.
Точный список выполненных и заблокированных проверок: [Verification](Docs/Verification.md).

## Открыть и запустить

1. В Unity Hub установите **6000.4.4f1** и активируйте лицензию штатным способом.
   Не помещайте лицензионные файлы, серийные номера или учётные данные в репозиторий.
2. Добавьте корень этого репозитория как существующий проект и откройте его.
3. Дождитесь Package Manager, компиляции и импорта FBX. Первый импорт запускает
   сборщик недостающего контента. Повторный запуск:
   **Tools → Voron → Build Character Prototype**.
4. Откройте `Assets/_Game/Scenes/Prototype/CharacterPrototype.unity` и нажмите Play.
   Если Unity запросит перезапуск для Input System, согласитесь и снова откройте сцену.
5. Подойдите к предмету, наведите центр камеры и нажмите **E**. **Tab / I** открывает
   инвентарь. Проверьте, что предмет исчез из комнаты и появился в списке.

Сборщик не заменяет существующие отредактированные сцены и prefab-ассеты.
Он создаёт базовый `Character_Base`, четыре variants, Player, четыре pickup-prefab,
данные персонажей/предметов, семь анимаций и два lighting profile. Все четыре NPC
в сцене — **технические экспонаты pipeline, не начало сюжета**.

### Управление

| Действие | Клавиатура/мышь | Gamepad (Xbox-обозначения) |
| --- | --- | --- |
| Перемещение / обзор | WASD / мышь | Left / right stick |
| Бег / присесть | Shift / Ctrl | L3 / R3 |
| Взаимодействие | E | A |
| Инвентарь | Tab или I | Y |
| Выбор предмета | Стрелки / мышь | D-pad / left stick |
| Закрыть инвентарь | Esc / Tab | B / Y |
| Фонарик | F | X |

Подробности: [Controls](Docs/Controls.md).

## Структура

```text
Assets/_Game/
  Art/                  # FBX, оригинальные лица/одежда, иконки и bitmap-шрифт
  Scripts/
    Characters/         # CharacterData, appearance, animation facade
    Core/               # Два сменных lighting profile
    Inventory/          # ItemData, атомарный InventoryStore, InventorySystem
    Interaction/        # IInteractable, raycast, pickup, NPC greeting
    Player/             # Input Actions, CharacterController, flashlight
    UI/                 # Canvas HUD и инвентарь
    Visuals/            # Low-resolution / dither post effect
  Editor/               # Воспроизводимая генерация Unity-ассетов и сцены
  Tests/                # Edit Mode и Play Mode
SourceArt/Characters/   # Редактируемый .blend и отчёт о геометрии/rig
Tools/                  # Генератор Blender, независимые тесты, API-компиляция
Docs/                   # Аудит, pipeline, проверка и evidence
```

`InventoryStore` — чистая C# модель с событиями; `InventorySystem` адаптирует её
к Unity. `ItemData` и `CharacterData` — ScriptableObject. UI подписывается на
изменения инвентаря. Взаимодействия получают явный context, без глобального
GameManager. Замена face texture не требует нового head mesh.

## Воспроизводимые проверки

Для Linux x86_64 предусмотрен idempotent setup:

```sh
mise install
bash Tools/setup.sh --with-unity
python3 Tools/sync_asset_meta.py --check
bash Tools/check.sh
```

Setup устанавливает portable Blender 4.5.3 и официальный Unity Editor. Он **не
активирует лицензию**. `check.sh` выполняет доменные тесты и компилирует C# против
реальных библиотек Unity; это не замена проверке игрового процесса.

С активированным редактором:

```sh
UNITY_EDITOR=/path/to/Unity bash Tools/run_unity_checks.sh
```

Затем обязательно выполните визуальный/интерактивный smoke test из
[Verification](Docs/Verification.md) в обычном Game view: `-nographics` не проверяет
рендеринг. Пересборка моделей и правила лицензирования ресурсов:
[Art Pipeline](Docs/ArtPipeline.md).

## Граница следующего этапа

Сначала закрыть настоящий цикл **Unity → Player → анимированный NPC → pickup →
Inventory UI**, включая gamepad. Crime Scene, полноценные диалоги, улики, save,
memory/flashback, монстр и сюжетные события не создаются до его подтверждения.
