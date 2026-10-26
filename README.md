# Custom Loot Pool

Server mod for **SPT 4.1.6+ (4.1.x)** that lets you add any item to the map loot pools, both in containers (crates, jackets, drawers...) and as loose loot (floor / shelves), from a page in the SPT server panel.

*(Português abaixo)*

## Features

- Search any item by name or ID, with item images.
- Choose maps, where it spawns (containers / loose loot) and the chance.
- **Similar items** mode: the item only goes where items of the same category already spawn. The page shows the loot pool, example items (hover one to see its chance) and the containers it reaches.
- **Pick loot pools**: choose any categories (Electronics, Keys, Jewelry, the whole Barter items group...) and the item joins the pools where those items spawn.
- **Simulate raids**: runs SPT's real loot generator and shows the average amount per raid on each map, even before saving.
- **Loot Atlas**: interactive Tarkov.dev maps with floors, container and loose-loot layers, landmark labels, and density cells. Compare eligible locations with 25 or 100 simulated raids using the editor settings. Scroll to zoom and drag to pan.
- **Loot quality**: make valuable or rare items more common in containers and loose loot, by price tier or by the game's own rarity, on all maps or only some. The amount of loot stays the same, and items on your list keep their exact chance.
- **Lots of Loot aware**: shows how much each map's loot is multiplied and lets you choose: let your items grow with Lots of Loot, or keep the vanilla amount.
- **More sources**: bot inventories (Scavs, PMCs, bosses and guards; backpack, pockets, vest) and airdrop crates.
- **At least 1 per raid**: one guaranteed copy at a random loose loot spot every raid.
- **Different % per map** and **rarity shortcuts** (Common / Typical / Rare / Legendary) based on the similar items.
- **When**: always, between two dates (every year) or only on weekends.
- **Presets** (Smugglers event), **export / import** to share settings, filter, duplicate, enable / disable all and collapsible cards.
- Changes apply from the next raid, no server restart needed.
- English and Portuguese UI.

## Install

Download the release zip and extract it into your game folder (the one that contains `SPT_Runtime`), so you end up with `SPT_Runtime/user/mods/CustomLootPool`.

The release ZIP includes a default `config.json` for new installations. When updating,
keep your existing `config.json` instead of overwriting it with the default.

## Usage

1. Start the SPT server.
2. Open `https://127.0.0.1:6969/customlootpool` (or the server panel at `https://127.0.0.1:6969` → Custom Loot Pool).
3. Add items, set maps / where / chance and click **Save**.

### Loot Atlas

Open **Loot Atlas** on an item card. **Eligible spots** counts possible physical
locations, not spawn probability. **Simulate raids** measures average item
occurrences per raid in a random sample. Zero observations do not establish
that spawning is impossible. Bots and airdrops are excluded.

Stock SPT stores (0, 0, 0) for every static container, because the game client
takes the position from the map scene. The mod ships real container positions
extracted from the game scenes (`wwwroot/maps/container-positions.json`), matched
to SPT by container instance Id. About 99.7% of the containers on every map are
covered; the rest, and containers on maps added by other mods, are reported as
unmapped instead of being drawn at the origin.

Source and floor filters apply to both the map and the ranked 80 m cells.
Landmark names indicate the nearest label, not a region boundary. All-floor
views overlap elevations; select a floor for useful building comparisons.

The release includes Tarkov.dev SVG artwork for Customs, Factory, Ground Zero,
Interchange, Lighthouse, Reserve, Shoreline, Streets and Woods. Other locations
show an unavailable message. Custom map variants can differ from this artwork.
There are no Dynamic Maps or QuestCodex dependencies or alternate map providers.

Map artwork by Shebuka and the Tarkov.dev SVG Maps contributors is licensed
separately under **CC BY-NC-SA 4.0**. Floor views are derived by extracting groups
and retaining a faint ground layer. Full artwork license, source revisions,
author links and modification details are in `wwwroot/maps/`. Map metadata
comes from Tarkov.dev under its MIT license. The mod code remains MIT.
`scripts/Import-TarkovMaps.ps1` reproduces the map import from pinned sources.

### Chances

- **Containers**: chance that each item rolled inside a matching container is this item.
- **Loose loot**: when a matching loot spot spawns, chance that it is this item.
- **Bots**: chance that each item rolled into the chosen inventory slots is this item.
- **Airdrops**: chance that an allowed crate contains one of this item. Select mixed supplies, weapons and armor, food and medical, barter supplies, or technical/event crates. Leave the selection empty to allow all types, including custom crates. This filter does not change airdrop frequency or the type SPT generates.

Everything is stored in `config.json` next to the DLL.

Chances that affect the same pool are solved together. When their sum reaches or
exceeds 100%, the configured items share the entire pool in proportion to their
requested chances. If the pool has no other valid candidates, its configured
items also share the full pool. Duplicate entries for the same item use the
highest currently applicable chance. Existing loose-loot candidates are updated
instead of being skipped.

Editors use revision checks: if another tab saves while you have local edits,
reload before saving. The stale editor cannot overwrite the newer config.

Simulation supports 4, 25 or 100 raids per map and includes all entries currently
on the page. It applies map spawn caps independently for each simulated raid.
Zero observations in a small random sample do not establish a zero spawn chance.
Bot/airdrop map filters use the request's session, with no shared last-map value.

### Loot quality

The **Loot quality** card multiplies the weight of every item in container and
loose loot pools, then keeps each pool's total, so the same number of items
spawn and only their mix changes.

- **Value**: price tiers in roubles (flea price, or handbook price when the flea
  has none). Defaults: under 10k ×1, 10k to 50k ×1, 50k to 150k ×2, 150k and up ×3.
  Items with no known price are left alone.
- **Rarity**: the game's own rarity (Common, Rare, Superrare). Default: Superrare ×2.
- ×1 keeps vanilla, ×0.5 halves a tier and ×0 removes it from the loot.
- Keys and keycards are left alone by default.

The boost runs before your list's chances, so configured items still take
exactly their share. In loose loot it only reweights items within each spawn
spot. Bigger items sometimes don't fit a container and the game rolls another.
The card's preview shows, per map, each tier's share and the average item value
before and after. With the defaults, the share of items worth 50k or more in
Customs containers roughly doubles. Simulations use the card's unsaved settings.

### Example: recreating the Smugglers event

Click the **Smugglers event** preset, or add **Locked case** (`6740987b89d5e1ddc603f4f0`) and **Case key** (`67449b6c89d5e1ddc603f504`) yourself.

- Locked case + Case key → Workbench → Opened case (4× Sealed box)
- Sealed box + Multitool → Workbench → Opened box (6 random items)

Both crafts are already unlocked by SPT.

## Building

Requires the .NET 10 SDK.

```
dotnet build -c Release
```

Copy `CustomLootPool.dll` and the `wwwroot` folder from `bin/Release/net10.0/` into `SPT_Runtime/user/mods/CustomLootPool/`.

For an update, copy the DLL and `wwwroot` only; keep your existing `config.json`.
For a new installation, the server creates the config automatically.

Regression tests require a local SPT 4.1 installation (default `C:\SPT`) and run
without starting the game or loading player profiles:

```powershell
dotnet test Tests/CustomLootPool.Tests.csproj -c Release -p:SPTPath=C:\SPT
```

See [CHANGELOG.md](CHANGELOG.md) for the latest fixes.

## Credits

Item images from [assets.tarkov.dev](https://tarkov.dev).

## License

MIT

---

## Português

Mod de servidor para o **SPT 4.1** que permite adicionar qualquer item ao lootpool dos mapas, em contêineres (caixas, jaquetas, gavetas...) e como loot solto (chão / prateleiras), por uma página no painel do servidor SPT.

### Recursos

- Busca de itens por nome ou ID, com imagem.
- Escolha de mapas, onde aparece (contêineres / loot solto) e a chance.
- Modo **itens parecidos**: o item só entra onde já spawnam itens da mesma categoria. A página mostra o lootpool, itens de exemplo (passe o mouse para ver a chance) e os contêineres alcançados.
- **Escolher lootpools**: escolha qualquer categoria (Eletrônicos, Chaves, Joias, o grupo inteiro de itens de troca...) e o item entra nos pools onde esses itens spawnam.
- **Simular raids**: roda o gerador de loot real do SPT e mostra a média por raid em cada mapa, mesmo antes de salvar.
- **Qualidade do loot**: deixa itens valiosos ou raros mais comuns nos contêineres e no loot solto, por faixa de preço ou pela raridade do próprio jogo, em todos os mapas ou só em alguns. A quantidade de loot não muda e os itens da sua lista mantêm a chance exata.
- **Compatível com Lots of Loot**: mostra quanto o loot de cada mapa é multiplicado e deixa você escolher: seus itens crescem junto com o Lots of Loot ou mantêm a quantidade do jogo original.
- **Mais fontes**: inventário dos bots (Scavs, PMCs, bosses e guardas; mochila, bolsos, colete) e caixas de airdrop.
- **Pelo menos 1 por raid**: uma cópia garantida em um ponto de loot solto aleatório a cada raid.
- **% diferente por mapa** e **atalhos de raridade** (Comum / Normal / Raro / Lendário) com base nos itens parecidos.
- **Quando**: sempre, entre duas datas (todo ano) ou só nos fins de semana.
- **Presets** (evento dos Smugglers), **exportar / importar** para compartilhar, filtro, duplicar, ligar / desligar todos e cards recolhíveis.
- As mudanças valem a partir do próximo raid, sem reiniciar o servidor.
- Interface em inglês e português.

### Instalação

Baixe o zip da release e extraia na pasta do jogo (a que contém `SPT_Runtime`), ficando `SPT_Runtime/user/mods/CustomLootPool`.

O ZIP inclui um `config.json` padrão para novas instalações. Ao atualizar, mantenha
seu `config.json` existente em vez de substituí-lo pelo padrão.

As chances são calculadas juntas por pool. Ao atingir ou ultrapassar 100%, os
itens configurados dividem todo o pool proporcionalmente. Se não houver outros
candidatos válidos, também dividem todo o pool. Entradas duplicadas usam a maior
chance aplicável. Itens que já aparecem no loot solto têm o peso ajustado.

Uma aba desatualizada não pode sobrescrever o que outra salvou: recarregue antes
de salvar novamente. A simulação permite 4, 25 ou 100 raids por mapa, usa os
limites de spawn e considera as outras entradas da tela. Uma amostra sem
ocorrências não prova que a chance seja zero.

### Uso

1. Inicie o servidor SPT.
2. Abra `https://127.0.0.1:6969/customlootpool` (ou o painel do servidor em `https://127.0.0.1:6969` → Custom Loot Pool).
3. Adicione itens, ajuste mapas / onde / chance e clique em **Salvar**.

### Qualidade do loot

O cartão **Qualidade do loot** multiplica o peso de cada item nos pools de
contêineres e de loot solto e mantém o total de cada pool. Aparece a mesma
quantidade de itens, só muda a mistura.

- **Valor**: faixas de preço em rublos (preço do flea, ou do handbook quando o
  item não tem preço no flea). Padrão: até 10k ×1, de 10k a 50k ×1, de 50k a
  150k ×2, a partir de 150k ×3. Itens sem preço conhecido não mudam.
- **Raridade**: a raridade do próprio jogo (Common, Rare, Superrare). Padrão: Superrare ×2.
- ×1 deixa como no jogo, ×0,5 corta a faixa pela metade e ×0 tira a faixa do loot.
- Chaves e keycards ficam de fora por padrão.

O reforço é aplicado antes das chances da sua lista, então os itens configurados
continuam com a fatia exata. No loot solto, ele só troca itens dentro de cada
ponto de spawn. Itens maiores às vezes não cabem no contêiner e o jogo sorteia
outro. A prévia do cartão mostra, por mapa, a fatia de cada faixa e o valor médio
por item antes e depois. Com o padrão, a fatia de itens de 50k ou mais nos
contêineres de Customs praticamente dobra. A simulação usa as configurações do
cartão mesmo antes de salvar.

### Exemplo: recriar o evento dos Smugglers

Clique no preset **Evento dos Smugglers**, ou adicione **Locked case** (`6740987b89d5e1ddc603f4f0`) e **Case key** (`67449b6c89d5e1ddc603f504`) você mesmo.

- Locked case + Case key → Workbench → Opened case (4× Sealed box)
- Sealed box + Multitool → Workbench → Opened box (6 itens aleatórios)

Os dois crafts já são liberados pelo próprio SPT.
