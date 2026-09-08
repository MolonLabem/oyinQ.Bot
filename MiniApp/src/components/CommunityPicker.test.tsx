import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { CommunityPicker } from "./CommunityPicker";

describe("shared community picker", () => {
  it("shows chat photos, mode labels and archived status using the main app cards", () => {
    const markup = renderToStaticMarkup(<CommunityPicker communities={[
      { key: "club", mode: "Club", name: "Клуб", avatarUrl: "data:image/jpeg;base64,AQ==" },
      { key: "camp", mode: "Camp", name: "Поход", statusLabel: "Завершён" },
    ]} choose={() => {}} />);
    expect(markup).toContain('class="card community-option"');
    expect(markup).toContain('src="data:image/jpeg;base64,AQ=="');
    expect(markup).toContain('class="mode-icon camp"');
    expect(markup).toContain("Кэмп · Завершён");
    expect(markup.match(/tabindex="0"/g)).toHaveLength(1);
  });
});
