import { Badge } from "./Ui";

export function GameTaxonomy({
  typeNames = [], categoryNames = [], mechanicNames = [], className = ""
}: {
  typeNames?: string[];
  categoryNames?: string[];
  mechanicNames?: string[];
  className?: string;
}) {
  if (!typeNames.length && !categoryNames.length && !mechanicNames.length) return null;

  return <section className={`content-section detail-section game-taxonomy ${className}`.trim()}>
    {typeNames.length > 0 && <TaxonomyGroup title="Тип">
      {typeNames.map(name => <Badge tone="neutral" key={name}>{name}</Badge>)}
    </TaxonomyGroup>}
    {categoryNames.length > 0 && <TaxonomyGroup title="Категории">
      {categoryNames.map(name => <span className="tag" key={name}>{name}</span>)}
    </TaxonomyGroup>}
    {mechanicNames.length > 0 && <TaxonomyGroup title="Механики">
      {mechanicNames.map(name => <span className="tag" key={name}>{name}</span>)}
    </TaxonomyGroup>}
  </section>;
}

function TaxonomyGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return <section className="taxonomy-group">
    <h2>{title}</h2>
    <div className="tag-list">{children}</div>
  </section>;
}
