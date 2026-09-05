import { api } from "../../api/client";
import { Card, ErrorState, Loading, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";

export function ChangelogPage({ back }: { back: () => void }) {
  const changelog = useAsync(() => api<{ markdown: string }>("/profile/changelog"), []);
  return <Page title="Что нового?" actions={<button onClick={back}>← В профиль</button>}>
    {changelog.loading ? <Loading /> : changelog.error ? <ErrorState message={changelog.error} retry={changelog.reload} />
      : changelog.data && <Card><ChangelogContent markdown={changelog.data.markdown} /></Card>}
  </Page>;
}

// The checked-in changelog uses headings, paragraphs, bullet lists and inline code.
// Render them as React text; never execute HTML from the document.
export function ChangelogContent({ markdown }: { markdown: string }) {
  const inline = (text: string) => text.split(/(`[^`]+`)/g).map((part, i) =>
    part.startsWith("`") && part.endsWith("`") ? <code key={i}>{part.slice(1, -1)}</code> : part);
  return <article className="changelog-content">{markdown.trim().split(/\r?\n\s*\r?\n/).map((block, i) => {
    const lines = block.split(/\r?\n/);
    if (lines.every(line => line.startsWith("- ")))
      return <ul key={i}>{lines.map((line, j) => <li key={j}>{inline(line.slice(2))}</li>)}</ul>;
    if (block.startsWith("### ")) return <h3 key={i}>{inline(block.slice(4))}</h3>;
    if (block.startsWith("## ")) return <h2 key={i}>{inline(block.slice(3))}</h2>;
    if (block.startsWith("# ")) return <p className="muted" key={i}>{inline(block.slice(2))}</p>;
    return <p key={i}>{inline(block)}</p>;
  })}</article>;
}
