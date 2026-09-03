export function GatheringTypeTag({ typeName }: { typeName?: string }) {
  return typeName
    ? <div className="tag-list"><span className="badge neutral">{typeName}</span></div>
    : null;
}

export function GatheringBggLink({ bggUrl, compact = false }: { bggUrl?: string; compact?: boolean }) {
  if (!bggUrl) return null;
  const link = <a href={bggUrl} target="_blank" rel="noreferrer">{compact ? "BGG" : "Открыть на BGG"}</a>;
  return compact ? link : <p>{link}</p>;
}

export function GatheringCollectionAction({ bggId, open }: { bggId?: number; open: (bggId: number) => void }) {
  return bggId && bggId > 0
    ? <button onClick={() => open(bggId)}>Посмотреть в коллекции</button>
    : null;
}
