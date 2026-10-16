# Custom Loot Pool

Server mod for **SPT 4.1** that lets you add any item to the map loot pools, both in containers (crates, jackets, drawers...) and as loose loot (floor / shelves), from a page in the SPT server panel.

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

Copy `CustomLootPool.dll`, `config.json` and the `wwwroot` folder from `bin/Release/net10.0/` into `SPT_Runtime/user/mods/CustomLootPool/`.

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

### Uso

1. Inicie o servidor SPT.
2. Abra `https://127.0.0.1:6969/customlootpool` (ou o painel do servidor em `https://127.0.0.1:6969` → Custom Loot Pool).
3. Adicione itens, ajuste mapas / onde / chance e clique em **Salvar**.

### Exemplo: recriar o evento dos Smugglers

Clique no preset **Evento dos Smugglers**, ou adicione **Locked case** (`6740987b89d5e1ddc603f4f0`) e **Case key** (`67449b6c89d5e1ddc603f504`) você mesmo.

- Locked case + Case key → Workbench → Opened case (4× Sealed box)
- Sealed box + Multitool → Workbench → Opened box (6 itens aleatórios)

Os dois crafts já são liberados pelo próprio SPT.
