import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { ChangelogContent } from "./ChangelogPage";

describe("раздел «Что нового?»", () => {
  it("показывает даты, разделы, списки и команды без исполнения HTML", () => {
    const html = renderToStaticMarkup(<ChangelogContent markdown={"# Изменения OyinQ\r\n\r\n## 2026-09-04\r\n\r\n### Сборы\r\n\r\n- Команда `/oiynq`\r\n- <script>alert(1)</script>"} />);
    expect(html).toContain("<h2>2026-09-04</h2>");
    expect(html).toContain("<h3>Сборы</h3>");
    expect(html).toContain("<ul><li>");
    expect(html).toContain("<code>/oiynq</code>");
    expect(html).not.toContain("<script>");
  });
});
