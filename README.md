# Custom Loot Pool

Server mod for **SPT 4.1.6+ (4.1.x)** that lets you add any item to the map loot pools, both in containers (crates, jackets, drawers...) and as loose loot (floor / shelves), from a page in the SPT server panel.

*(Português abaixo)*

## Features

- Search any item by name or ID, with item images.
- Choose maps, where it spawns (containers / loose loot) and the chance.
- **Similar items** mode: the item only goes where items of the same category already spawn. The page shows the loot pool, example items (hover one to see its chance) and the containers it reaches.
- **Pick loot pools**: choose any categories (Electronics, Keys, Jewelry, the whole Barter items group...) and the item joins the pools where those items spawn.
- **Simulate raids**: runs SPT's real loot generator and shows the average amount per raid on each map, even before saving.
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

Starting with 1.2.1, the release ZIP omits `config.json` so an update preserves your
existing settings. A new installation creates its default config on first start.

## Usage

1. Start the SPT server.
2. Open `https://127.0.0.1:6969/customlootpool` (or the server panel at `https://127.0.0.1:6969` → Custom Loot Pool).
3. Add items, set maps / where / chance and click **Save**.

### Chances

- **Containers**: chance that each item rolled inside a matching container is this item.
- **Loose loot**: when a matching loot spot spawns, chance that it is this item.
- **Bots**: chance that each item rolled into the chosen inventory slots is this item.
- **Airdrops**: chance that a crate contains one of this item.

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

See [CHANGELOG.md](CHANGELOG.md) for the 1.2.1 fixes.

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

A partir da versão 1.2.1, o ZIP não inclui `config.json`: atualizar preserva suas
configurações. Na primeira instalação, o arquivo é criado ao iniciar o servidor.

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

### Exemplo: recriar o evento dos Smugglers

Clique no preset **Evento dos Smugglers**, ou adicione **Locked case** (`6740987b89d5e1ddc603f4f0`) e **Case key** (`67449b6c89d5e1ddc603f504`) você mesmo.

- Locked case + Case key → Workbench → Opened case (4× Sealed box)
- Sealed box + Multitool → Workbench → Opened box (6 itens aleatórios)

Os dois crafts já são liberados pelo próprio SPT.

## Validation for 1.2.1

Built with .NET SDK 10.0.402 and SPTushonka 4.1.6 packages. All 26 regression tests pass against SPT 4.1.6 assemblies. A clean, isolated SPT 4.1.6 server loads the mod, registers hooks on 13 maps and serves the configuration page and CSS. No existing profiles were used. In-game raids and Fika sessions have not been verified for this release.
