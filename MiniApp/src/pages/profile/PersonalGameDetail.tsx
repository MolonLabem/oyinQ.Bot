import type { PersonalCollectionItem } from "../../api/types";
import { GameDescription, GameOverview } from "../../components/GameOverview";
import { BackButton, Notice, Page } from "../../components/Ui";
import { useBackButton } from "../../hooks/useBackButton";

// Personal details use the authenticated owner's saved collection, without a community or provider request.
export function PersonalGameDetail({ item, expansions, open, back }: {
  item: PersonalCollectionItem; expansions: PersonalCollectionItem[];
  open: (item: PersonalCollectionItem) => void; back: () => void;
}) {
  useBackButton(true, back);
  const game = { ...item.snapshot, bggId: item.bggId };
  return <div className="game-detail-screen"><Page preserveScroll actions={<BackButton onClick={back} />}>
    <GameOverview game={game}><p className="availability">{item.itemType === "Expansion" ? "Дополнение в вашей коллекции" : "Есть у вас"}</p></GameOverview>
    {!game.complexityInfo && <Notice>Сложность не указана в сохранённых данных игры.</Notice>}
    <GameDescription game={game} />
    {expansions.length > 0 && <section className="content-section detail-section"><h2>Ваши дополнения</h2><ul className="catalog-expansion-list">{expansions.map(expansion => <li key={expansion.bggId}><button className="catalog-expansion-option" onClick={() => open(expansion)}>{expansion.snapshot.name}</button></li>)}</ul></section>}
  </Page></div>;
}
