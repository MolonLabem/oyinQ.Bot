import { ReleaseAnnouncementPage } from "./ReleaseAnnouncementPage";
import { useEffect, useMemo, useState } from "react";
import { api, download, json } from "../../api/client";
import { RecruitmentSettings } from "./RecruitmentSettings";
import { GatheringDashboard } from "../../components/GatheringDashboard";
import { GatheringDetails } from "../gatherings/GatheringsPage";
import type { AdminCamp, AdminClub, AdminOverview, Administrator, CampAdminParticipants, CampParticipantDmResult, ClubCollectionState, ClubGame, Community, EligibleAdministrator, LockedAdminCommunity, PeerTicket, PostingTopicSettings } from "../../api/types";
import { Badge, BggAttribution, Card, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, GamePicker, searchGames } from "../../components/GamePicker";
import { TimeZoneSelect } from "../../components/TimeZoneSelect";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { campStatusLabel, currentLocalMinute, formatInstant, formatDate, plural } from "../../app/format";
import { bggImportProgressText, clubImportResultText, type BggImportStage } from "../../app/bggImportProgress";
import { hasAvailableExpansions, toggleExpansionList } from "./expansionAvailability";
import { postingTopicTitle, selectablePostingTopics, shouldShowPostingTopic } from "./postingTopicState";
import { campDateValidation, cancellationConfirmation, canCancelCamp, canDeleteCommunity, deletionConfirmation, type CommunityKind } from "./communityLifecycleState";

type Section = "release" | "clubs" | "camps" | "gatherings" | "administrators" | "export" | "collection" | "participants";
type CommunityCreated = {
  id: number;
  telegramOnboardingSent: boolean;
  warning?: string;
};

export function AdminPage({ bggAvailable, isSuperAdmin }: { bggAvailable: boolean; isSuperAdmin: boolean }) {
  const [section, setSection] = useState<Section>("clubs");
  const [clubId, setClubId] = useState<number>();
  const [adminChat, setAdminChat] = useState<{ key: string; name: string }>();
  const [participantCamp, setParticipantCamp] = useState<{ id: number; name: string }>();
  useEffect(
    () => telegram.back(section === "collection" || section === "administrators" || section === "participants", () => setSection(section === "participants" ? "camps" : "clubs")),
    [section],
  );
  if (section === "collection" && clubId) return <ClubCollection clubId={clubId} bggAvailable={bggAvailable} back={() => setSection("clubs")} />;
  const nav = [
    { id: "clubs", label: "Клубы", icon: "♣" },
    { id: "camps", label: "Кэмпы", icon: "⛺" },
    { id: "gatherings", label: "Контроль сборов", icon: "⚑" },
    { id: "export", label: "Экспорт", icon: "↕" },
  ] as const;
  const openAdmins = (key: string, name: string) => {
    setAdminChat({ key, name });
    setSection("administrators");
  };
  return (
    <div className="admin-shell">
      <aside className="admin-sidebar">
        <h1>Администрирование</h1>
        <nav>
          {isSuperAdmin && <button className={section === "release" ? "active" : ""} onClick={() => setSection("release")}>Обновление OyinQ</button>}
          {nav.map((item) => (
            <button key={item.id} className={section === item.id ? "active" : ""} onClick={() => setSection(item.id)}>
              <span aria-hidden>{item.icon}</span>
              {item.label}
            </button>
          ))}
        </nav>
      </aside>
      <div className="admin-main">
        {section === "release" && isSuperAdmin ? <ReleaseAnnouncementPage /> : section === "clubs" ? (
          <Communities
            view="clubs"
            isSuperAdmin={isSuperAdmin}
            manage={(id) => {
              setClubId(id);
              setSection("collection");
            }}
            manageAdmins={openAdmins}
          />
        ) : section === "camps" ? (
          <Communities
            view="camps"
            isSuperAdmin={isSuperAdmin}
            manage={(id) => {
              setClubId(id);
              setSection("collection");
            }}
            manageAdmins={openAdmins}
            manageParticipants={(camp) => {
              setParticipantCamp({ id: camp.id, name: camp.name });
              setSection("participants");
            }}
          />
        ) : section === "gatherings" ? (
          <GatheringOperationsPage />
        ) : section === "participants" && participantCamp ? (
          <CampParticipants campId={participantCamp.id} campName={participantCamp.name} back={() => setSection("camps")} />
        ) : section === "administrators" && adminChat ? (
          <Administrators communityKey={adminChat.key} communityName={adminChat.name} back={() => setSection("clubs")} />
        ) : (
          <Export isSuperAdmin={isSuperAdmin} />
        )}
        <BggAttribution />
      </div>
    </div>
  );
}

function GatheringOperationsPage() {
  const overview = useAsync(() => api<AdminOverview>("/admin/overview"), []);
  const [communityKey, setCommunityKey] = useState("");
  const [selected, setSelected] = useState<{ communityKey: string; id: string }>();
  const communities = useMemo(() => overview.data ? [
    ...overview.data.clubs.filter(item => item.isActive).map<Community>(item => ({ key: item.communityKey, name: item.name, mode: "Club", timeZoneId: item.timeZoneId })),
    ...overview.data.camps.filter(item => item.status === "Active").map<Community>(item => ({ key: item.communityKey, name: item.name, mode: "Camp", timeZoneId: item.timeZoneId, startsAtUtc: item.startsAtUtc, endsAtUtc: item.endsAtUtc, startDate: item.startDate, endDate: item.endDate }))
  ] : [], [overview.data]);
  useEffect(() => { if (!communities.some(item => item.key === communityKey)) setCommunityKey(communities[0]?.key ?? ""); }, [communities, communityKey]);
  useEffect(() => telegram.back(Boolean(selected), () => setSelected(undefined)), [selected]);
  const selectedCommunity = selected ? communities.find(item => item.key === selected.communityKey) : undefined;
  if (selected && selectedCommunity) return <GatheringDetails readOnly community={selectedCommunity} id={selected.id}
    onBack={() => setSelected(undefined)} onCancelled={() => setSelected(undefined)} editRegistration={() => {}} openCollection={() => {}} />;
  return <Page title="Контроль сборов" subtitle="Набор игроков, коробки и проблемы доставки">
    {overview.loading ? <Loading /> : overview.error ? <ErrorState message={overview.error} retry={overview.reload} /> : !communities.length
      ? <Empty>Нет активных сообществ со сборами.</Empty> : <>
        <Field label="Сообщество" hint="Показаны только ситуации, в которых может понадобиться действие администратора.">
          <select value={communityKey} onChange={event => setCommunityKey(event.target.value)}>{communities.map(item =>
            <option key={item.key} value={item.key}>{item.name} · {item.mode === "Club" ? "Клуб" : "Кэмп"}</option>)}</select>
        </Field>
        {communityKey && <GatheringDashboard communityKey={communityKey} open={(key, id) => setSelected({ communityKey: key, id })} />}
      </>}
  </Page>;
}

function Communities({ manage, manageAdmins, manageParticipants, view, isSuperAdmin }: { manage: (id: number) => void; manageAdmins: (key: string, name: string) => void; manageParticipants?: (camp: AdminCamp) => void; view: "clubs" | "camps"; isSuperAdmin: boolean }) {
  const state = useAsync(() => api<AdminOverview>("/admin/overview"), []);
  const [create, setCreate] = useState<"club" | "camp">();
  const [createChat, setCreateChat] = useState<LockedAdminCommunity>();
  const [editing, setEditing] = useState<AdminClub>();
  const [editingCamp, setEditingCamp] = useState<AdminCamp>();
  const [mutationError, setMutationError] = useState<string>();
  const [mutationKey, setMutationKey] = useState<string>();
  useEffect(
    () =>
      telegram.back(Boolean(create || editing || editingCamp), () => {
        setCreate(undefined);
        setCreateChat(undefined);
        setEditing(undefined);
        setEditingCamp(undefined);
      }),
    [create, createChat, editing, editingCamp],
  );
  async function updateCamp(id: number, status: string) {
    if (mutationKey) return;
    setMutationKey(`camp-${id}`);
    setMutationError(undefined);
    try {
      await changeCamp(id, status, state.data?.camps.find((camp) => camp.id === id)?.name ?? "кэмп", state.reload);
    } catch (e) {
      setMutationError(e instanceof Error ? e.message : String(e));
    } finally {
      setMutationKey(undefined);
    }
  }
  async function deleteCommunity(kind: CommunityKind, id: number, name: string) {
    if (mutationKey) return;
    if (!(await telegram.confirm(deletionConfirmation(kind, name)))) return;
    setMutationKey(`${kind}-${id}`);
    setMutationError(undefined);
    try {
      await api(`/admin/${kind}/${id}`, json("DELETE"));
      telegram.success(kind === "clubs" ? "Клуб удалён из OyinQ" : "Кэмп удалён из OyinQ");
      state.reload();
    } catch (error) {
      setMutationError(error instanceof Error ? error.message : String(error));
    } finally {
      setMutationKey(undefined);
    }
  }
  if (create === "club")
    return (
      <CreateClub
        knownChat={createChat}
        done={() => {
          setCreate(undefined);
          setCreateChat(undefined);
          state.reload();
        }}
      />
    );
  if (create === "camp")
    return (
      <CreateCamp
        overview={state.data}
        knownChat={createChat}
        done={() => {
          setCreate(undefined);
          setCreateChat(undefined);
          state.reload();
        }}
      />
    );
  if (editing)
    return (
      <EditClub
        club={editing}
        overview={state.data}
        done={() => {
          setEditing(undefined);
          state.reload();
        }}
      />
    );
  if (editingCamp)
    return (
      <EditCamp
        camp={editingCamp}
        overview={state.data}
        done={() => {
          setEditingCamp(undefined);
          state.reload();
        }}
      />
    );
  const locked = state.data?.lockedCommunities.filter((item) =>
    !item.communityKey && isSuperAdmin ? true : item.mode === (view === "clubs" ? "Club" : "Camp")) ?? [];
  return (
    <Page
      title={view === "clubs" ? "Клубы" : "Кэмпы"}
      subtitle={view === "clubs" ? "Коллекции и настройки клубных сообществ" : "События, даты и базовые коллекции"}
      actions={
        isSuperAdmin ? (
          <button className="primary" onClick={() => setCreate(view === "clubs" ? "club" : "camp")}>
            {view === "clubs" ? "Новый клуб" : "Новый кэмп"}
          </button>
        ) : undefined
      }
    >
      {mutationError && <Notice kind="danger">{mutationError}</Notice>}
      {state.loading ? (
        <Loading />
      ) : state.error ? (
        <ErrorState message={state.error} retry={state.reload} />
      ) : (
        <>
          {locked.map((item) => (
            <Card key={`${item.telegramChatId}-${view}`}>
              <h3>{item.name}</h3>
              {item.communityKey ? (
                <>
                  <Notice kind="warning">Вы являетесь администратором этого чата, но доступ к управлению OyinQ ещё не выдан.</Notice>
                  <Badge tone="neutral">🔒 Доступ не выдан</Badge>
                </>
              ) : (
                <>
                  <Notice>Бот уже видит эту Telegram-группу. Настройте её в OyinQ без необходимости вступать в группу.</Notice>
                  <button className="primary" onClick={() => { setCreateChat(item); setCreate(view === "clubs" ? "club" : "camp"); }}>
                    {view === "clubs" ? "Создать клуб" : "Создать кэмп"}
                  </button>
                </>
              )}
            </Card>
          ))}
          {view === "clubs" ? (
            !state.data?.clubs.length && !locked.length ? (
              <Empty>Доступных клубов пока нет.</Empty>
            ) : (
              <div className="admin-entity-grid">
                {state.data?.clubs.map((club) => (
                  <Card key={club.id}>
                    <div className="row">
                      <div>
                        <h3>{club.name}</h3>
                        <p className="muted">{club.telegramTitle}</p>
                        <p>{plural(club.gameCount, "игра", "игры", "игр")}</p>
                      </div>
                      <Badge tone={club.isActive ? "success" : "neutral"}>{club.isActive ? "Активен" : "Архив"}</Badge>
                    </div>
                    <div className="admin-card-actions">
                      <button onClick={() => manage(club.id)}>Коллекция</button>
                      <button onClick={() => setEditing(club)}>Настройки</button>
                      <button onClick={() => manageAdmins(club.communityKey, club.name)}>Администраторы</button>
                      {canDeleteCommunity(isSuperAdmin) && <button disabled={Boolean(mutationKey)} className="danger ghost" onClick={() => deleteCommunity("clubs", club.id, club.name)}>{mutationKey === `clubs-${club.id}` ? "Удаляем…" : "Удалить из OyinQ"}</button>}
                    </div>
                    <details className="technical">
                      <summary>Техническая информация</summary>
                      <p>Telegram ID: <code>{club.telegramChatId}</code></p>
                      <p>Часовой пояс: {club.timeZoneId}</p>
                      <p>Ревизия: {club.collectionRevision}</p>
                      <small>Обновлено {new Date(club.updatedAt).toLocaleString("ru-RU")}</small>
                    </details>
                  </Card>
                ))}
              </div>
            )
          ) : !state.data?.camps.length && !locked.length ? (
            <Empty>Доступных кэмпов пока нет.</Empty>
          ) : (
            <div className="admin-entity-grid">
              {state.data?.camps.map((camp) => (
                <Card key={camp.id}>
                  <div className="row">
                    <div>
                      <h3>{camp.name}</h3>
                      <p>
                        {camp.startsAtUtc ? formatInstant(camp.startsAtUtc, camp.timeZoneId) : "Не указано"} — {camp.endsAtUtc ? formatInstant(camp.endsAtUtc, camp.timeZoneId) : "Не указано"}
                      </p>
                      <p className="muted">{camp.sourceClubName ? `Коллекция: ${camp.sourceClubName}` : "Без исходного клуба"}</p>
                      <p>
                        {plural(camp.registrations, "регистрация", "регистрации", "регистраций")} · {plural(camp.gatherings, "сбор", "сбора", "сборов")}
                      </p>
                    </div>
                    <Badge tone={camp.status === "Active" ? "success" : "neutral"}>{campStatusLabel(camp.status)}</Badge>
                  </div>
                  <div className="admin-card-actions">
                    <button onClick={() => manageParticipants?.(camp)}>Участники</button>
                    <button onClick={() => setEditingCamp(camp)}>Настройки</button>
                    <button onClick={() => manageAdmins(camp.communityKey, camp.name)}>Администраторы</button>
                    {camp.status === "Draft" && <button disabled={Boolean(mutationKey)} className="primary" onClick={() => updateCamp(camp.id, "Active")}>{mutationKey === `camp-${camp.id}` ? "Активируем…" : "Активировать"}</button>}
                  </div>
                  {(canCancelCamp(camp.status) || canDeleteCommunity(isSuperAdmin)) && <details className="danger-actions">
                    <summary>Опасные действия</summary>
                    <p className="muted">Отмена означает, что событие не состоится. Удаление отключает кэмп в OyinQ, но не удаляет Telegram-группу и историю.</p>
                    <div className="admin-card-actions">
                      {canCancelCamp(camp.status) && <button disabled={Boolean(mutationKey)} className="danger ghost" onClick={() => updateCamp(camp.id, "Cancelled")}>{mutationKey === `camp-${camp.id}` ? "Отменяем…" : "Отменить проведение"}</button>}
                      {canDeleteCommunity(isSuperAdmin) && <button disabled={Boolean(mutationKey)} className="danger ghost" onClick={() => deleteCommunity("camps", camp.id, camp.name)}>{mutationKey === `camps-${camp.id}` ? "Удаляем…" : "Удалить из OyinQ"}</button>}
                    </div>
                  </details>}
                  <details className="technical">
                    <summary>Техническая информация</summary>
                    <p>Telegram ID: <code>{camp.telegramChatId}</code></p>
                    <p>Часовой пояс: {camp.timeZoneId}</p>
                  </details>
                </Card>
              ))}
            </div>
          )}
        </>
      )}
    </Page>
  );
}

function CampParticipants({ campId, campName, back }: { campId: number; campName: string; back: () => void }) {
  const state = useAsync(() => api<CampAdminParticipants>(`/admin/camps/${campId}/participants`), [campId]);
  const [sending, setSending] = useState(false);
  const [sendError, setSendError] = useState<string>();
  async function sendToMe() {
    if (sending) return;
    setSending(true);
    setSendError(undefined);
    try {
      const result = await api<CampParticipantDmResult>(`/admin/camps/${campId}/participants/send-to-me`, json("POST"));
      telegram.success(result.participantCount
        ? `Список отправлен: ${plural(result.participantCount, "участник", "участника", "участников")}`
        : "Пустой список отправлен");
    } catch (error) {
      setSendError(error instanceof Error ? error.message : String(error));
    } finally {
      setSending(false);
    }
  }
  return (
    <Page
      title="Участники кэмпа"
      subtitle={state.data?.campName ?? campName}
      actions={<button onClick={back}>Назад</button>}
    >
      <Card>
        <div className="row">
          <div>
            <strong>Список в личный чат</strong>
            <p className="muted">Бот отправит эти данные вам в Telegram.</p>
          </div>
          <button className="primary" disabled={sending || state.loading || Boolean(state.error)} onClick={sendToMe}>
            {sending ? "Отправляем…" : "Отправить мне"}
          </button>
        </div>
        {sendError && <Notice kind="danger">{sendError}</Notice>}
      </Card>
      {state.loading ? (
        <Loading />
      ) : state.error ? (
        <ErrorState message={state.error} retry={state.reload} />
      ) : !state.data?.participants.length ? (
        <Empty>Пока никто не зарегистрировался.</Empty>
      ) : (
        <div className="admin-entity-grid">
          {state.data.participants.map((participant) => (
            <Card key={participant.participantId}>
              <div className="row">
                <h3><ContactLink url={participant.contactUrl}>{participant.displayName}</ContactLink></h3>
                <Badge tone={participant.needsAccommodation ? "warning" : "neutral"}>
                  {participant.needsAccommodation ? "Нужно жильё" : "Жильё не нужно"}
                </Badge>
              </div>
              {participant.telegramUsername && <p className="muted">@{participant.telegramUsername}</p>}
              <p><strong>Город:</strong> {participant.city || "не указан"}</p>
              <p><strong>Даты:</strong> {participant.selectedDates.length ? participant.selectedDates.map(formatDate).join(", ") : "не указаны"}</p>
            </Card>
          ))}
        </div>
      )}
    </Page>
  );
}

function EditClub({ club, overview, done }: { club: AdminClub; overview?: AdminOverview; done: () => void }) {
  const [name, setName] = useState(club.name);
  const [zone, setZone] = useState(club.timeZoneId);
  const [active, setActive] = useState(club.isActive);
  const [source, setSource] = useState<number | "">("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const sourceClubs = overview?.clubs.filter((item) => item.id !== club.id) ?? [];
  const sourceClub = sourceClubs.find((item) => item.id === source);
  async function save() {
    if (busy) return;
    if (!active && club.isActive && !(await telegram.confirm("Архивировать клуб? Он исчезнет из выбора активных сообществ."))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/clubs/${club.id}`, json("PUT", { name, timeZoneId: zone, isActive: active }));
      telegram.success("Настройки клуба сохранены");
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function copyCollection() {
    if (busy || !sourceClub) return;
    if (!(await telegram.confirm(`Заменить ${plural(club.gameCount, "игру", "игры", "игр")} клуба «${club.name}» коллекцией «${sourceClub.name}» (${plural(sourceClub.gameCount, "игра", "игры", "игр")})?`))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(
        `/admin/clubs/${club.id}/collection/from-club`,
        json("POST", {
          sourceClubId: sourceClub.id,
          expectedRevision: club.collectionRevision,
        }),
      );
      telegram.success("Коллекция клуба скопирована");
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Настройки клуба" actions={<button onClick={done}>Назад</button>}>
      <Card className="form-grid">
        <Field label="Название">
          <input value={name} maxLength={160} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Часовой пояс" hint={club.gatherings > 0 ? "Нельзя изменить после создания первого сбора" : "Время сборов будет показано в этом часовом поясе"}>
          <TimeZoneSelect value={zone} onChange={setZone} disabled={club.gatherings > 0} />
        </Field>
        <label className="check">
          <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} />
          Клуб активен
        </label>
        <button className="primary" disabled={busy || !name.trim() || !zone.trim()} onClick={save}>
          {busy ? "Сохраняем…" : "Сохранить настройки"}
        </button>
      </Card>
      <PostingTopicSetting communityKey={club.communityKey} />
      <RecruitmentSettings communityKey={club.communityKey} />
      <Card className="form-grid">
        <h2>Скопировать коллекцию</h2>
        <Notice>Коллекция выбранного клуба полностью заменит текущую. Сборы, уже созданные из старой коллекции, сохранят свои снимки игр.</Notice>
        {sourceClubs.length ? (
          <>
            <Field label="Клуб-источник" hint={sourceClub ? `${plural(sourceClub.gameCount, "игра", "игры", "игр")} в коллекции` : "Выберите клуб, коллекцию которого нужно скопировать"}>
              <select value={source} onChange={(event) => setSource(event.target.value ? +event.target.value : "")}>
                <option value="">Выберите клуб</option>
                {sourceClubs.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name} · {item.gameCount}
                  </option>
                ))}
              </select>
            </Field>
            <button disabled={busy || !sourceClub} onClick={copyCollection}>
              {busy ? "Копируем…" : "Заменить коллекцию"}
            </button>
          </>
        ) : (
          <Notice kind="warning">Других клубов пока нет, поэтому копировать коллекцию неоткуда.</Notice>
        )}
      </Card>
      {error && <Notice kind="danger">{error}</Notice>}
    </Page>
  );
}

function EditCamp({ camp, overview, done }: { camp: AdminCamp; overview?: AdminOverview; done: () => void }) {
  const [name, setName] = useState(camp.name);
  const [zone, setZone] = useState(camp.timeZoneId);
  const [start, setStart] = useState(camp.startsAtUtc ? currentLocalMinute(camp.timeZoneId, new Date(camp.startsAtUtc)) : "");
  const [end, setEnd] = useState(camp.endsAtUtc ? currentLocalMinute(camp.timeZoneId, new Date(camp.endsAtUtc)) : "");
  const [source, setSource] = useState<number | "">(camp.sourceClubId ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [dateErrors, setDateErrors] = useState<{ start?: string; end?: string }>({});
  const sourceClub = overview?.clubs.find((item) => item.id === source);
  async function save() {
    if (busy) return;
    const validation = campDateValidation(start, end);
    setDateErrors(validation);
    if (validation.start || validation.end) { setError(undefined); return; }
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/camps/${camp.id}`, json("PUT", { name, timeZoneId: zone, startsAtLocal: start, endsAtLocal: end }));
      telegram.success("Настройки кэмпа сохранены");
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function copyCollection() {
    if (busy || !sourceClub) return;
    if (!(await telegram.confirm(`Заменить базовую коллекцию кэмпа снимком «${sourceClub.name}» (${plural(sourceClub.gameCount, "игра", "игры", "игр")})?`))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/camps/${camp.id}/base-collection/from-club`, json("POST", { sourceClubId: sourceClub.id }));
      telegram.success("Базовая коллекция кэмпа обновлена");
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Настройки кэмпа" actions={<button onClick={done}>Назад</button>}>
      <Card className="form-grid">
        <Field label="Название">
          <input value={name} maxLength={160} onChange={(event) => setName(event.target.value)} />
        </Field>
        <div className="date-range">
          <Field label="Начало кэмпа" error={dateErrors.start}>
            <input type="datetime-local" value={start} onChange={(event) => { setStart(event.target.value); setDateErrors({}); }} />
          </Field>
          <Field label="Окончание кэмпа" error={dateErrors.end}>
            <input type="datetime-local" min={start} value={end} onChange={(event) => { setEnd(event.target.value); setDateErrors({}); }} />
          </Field>
        </div>
        <Field label="Часовой пояс" hint={camp.gatherings > 0 ? "Нельзя изменить после создания первого сбора" : "Выберите город с тем же местным временем"}>
          <TimeZoneSelect value={zone} onChange={setZone} disabled={camp.gatherings > 0} />
        </Field>
        <Notice>Изменение дат не удаляет данные. Все существующие регистрации и сборы должны помещаться в новый диапазон.</Notice>
        <button className="primary" disabled={busy || !name.trim() || !zone.trim()} onClick={save}>
          {busy ? "Сохраняем…" : "Сохранить настройки"}
        </button>
      </Card>
      <PostingTopicSetting communityKey={camp.communityKey} />
      <RecruitmentSettings communityKey={camp.communityKey} />
      <Card className="form-grid">
        <h2>Базовая коллекция</h2>
        {camp.status !== "Draft" ? (
          <Notice>Снимок базовой коллекции зафиксирован при активации кэмпа и больше не изменяется.</Notice>
        ) : (
          <>
            <Notice>Будет создан новый снимок коллекции клуба. Игры участников и уже созданные сборы не изменятся.</Notice>
            {overview?.clubs.length ? (
              <>
                <Field label="Клуб-источник" hint={sourceClub ? `${plural(sourceClub.gameCount, "игра", "игры", "игр")} в коллекции` : "Выберите клуб с нужной коллекцией"}>
                  <select value={source} onChange={(event) => setSource(event.target.value ? +event.target.value : "")}>
                    <option value="">Выберите клуб</option>
                    {overview.clubs.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name} · {item.gameCount}
                      </option>
                    ))}
                  </select>
                </Field>
                <button disabled={busy || !sourceClub} onClick={copyCollection}>
                  {busy ? "Копируем…" : "Обновить базовую коллекцию"}
                </button>
              </>
            ) : (
              <Notice kind="warning">Клубов пока нет, поэтому выбрать базовую коллекцию невозможно.</Notice>
            )}
          </>
        )}
      </Card>
      {error && <Notice kind="danger">{error}</Notice>}
    </Page>
  );
}

function PostingTopicSetting({ communityKey }: { communityKey: string }) {
  const state = useAsync(() => api<PostingTopicSettings>(`/admin/communities/${communityKey}/posting-topic`), [communityKey]);
  const [selected, setSelected] = useState<number | "">("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  useEffect(() => setSelected(state.data?.messageThreadId ?? ""), [state.data?.messageThreadId]);
  if (state.loading) return <Card><Loading /></Card>;
  if (state.error) return <Card><ErrorState message={state.error} retry={state.reload} /></Card>;
  if (!state.data || !shouldShowPostingTopic(state.data)) return null;
  const topics = selectablePostingTopics(state.data);
  async function save(messageThreadId: number | null) {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/communities/${communityKey}/posting-topic`, json("PUT", { messageThreadId }));
      telegram.success(messageThreadId === null ? "Будет использоваться основная тема" : "Тема для сообщений сохранена");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Card className="form-grid">
      <h2>💬 Тема для сообщений</h2>
      {state.data.needsSelection ? (
        <Notice kind="warning">Выбранная тема больше недоступна. Выберите другую.</Notice>
      ) : state.data.messageThreadId === null ? (
        <Notice>Сообщения публикуются в основной теме.</Notice>
      ) : (
        <Notice kind="success">Текущая тема: <strong>{postingTopicTitle(state.data)}</strong></Notice>
      )}
      {topics.length > 0 && (
        <>
          <Field label="Известные темы" hint="OyinQ узнаёт темы из сообщений, которые получает в группе">
            <select value={selected} onChange={(event) => setSelected(event.target.value ? +event.target.value : "")}>
              <option value="">Выберите тему</option>
              {topics.map((topic) => <option key={topic.messageThreadId} value={topic.messageThreadId}>{topic.name}</option>)}
            </select>
          </Field>
          <button className="primary" disabled={busy || selected === ""} onClick={() => selected !== "" && save(selected)}>
            {busy ? "Сохраняем…" : "Использовать выбранную тему"}
          </button>
        </>
      )}
      <Notice>Если нужной темы нет в списке, откройте её в Telegram и отправьте там команду <code>/oiynq topic</code>.</Notice>
      {state.data.messageThreadId !== null && <button disabled={busy} onClick={() => save(null)}>Основная тема</button>}
      {error && <Notice kind="danger">{error}</Notice>}
    </Card>
  );
}

async function changeCamp(id: number, status: string, name: string, reload: () => void) {
  if (status === "Cancelled" && !(await telegram.confirm(cancellationConfirmation(name)))) return;
  await api(`/admin/camps/${id}/status`, json("POST", { status }));
  telegram.success(status === "Active" ? "Кэмп активирован" : "Проведение кэмпа отменено");
  reload();
}

function CreateClub({ knownChat, done }: { knownChat?: LockedAdminCommunity; done: () => void }) {
  const [name, setName] = useState(knownChat?.name ?? "");
  const [zone, setZone] = useState(Intl.DateTimeFormat().resolvedOptions().timeZone);
  const [selection, setSelection] = useState<PeerTicket>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  async function choose() {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const ticket = await selectPeer("CreateClubChat");
      setSelection(ticket);
      if (!name.trim() && ticket.result?.chat?.title) setName(ticket.result.chat.title);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function create() {
    if (busy || (!selection && !knownChat)) return;
    setBusy(true);
    setError(undefined);
    try {
      const result = await api<CommunityCreated>(
        "/admin/clubs",
        json("POST", {
          selectionId: selection?.publicId,
          knownTelegramChatId: knownChat?.telegramChatId,
          name,
          timeZoneId: zone,
        }),
      );
      telegram.success("Клуб создан");
      if (result.warning) {
        telegram.warning();
        window.alert(result.warning);
      }
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Новый клуб" actions={<button onClick={done}>Назад</button>}>
      <Card>
        {!selection && !knownChat ? (
          <>
            <Notice>Сначала выберите Telegram-группу. Бот и вы должны быть её администраторами.</Notice>
            <button className="primary" disabled={busy} onClick={choose}>
              {busy ? "Ожидаем Telegram…" : "Выбрать группу"}
            </button>
          </>
        ) : (
          <>
            <Notice kind="success">
              Выбрана группа: <strong>{knownChat?.name ?? selection?.result?.chat?.title ?? "Telegram-группа"}</strong>
            </Notice>
            <Field label="Название" hint="Можно изменить название, полученное из Telegram">
              <input value={name} maxLength={160} onChange={(e) => setName(e.target.value)} />
            </Field>
            <Field label="Часовой пояс">
              <TimeZoneSelect value={zone} onChange={setZone} />
            </Field>
            <h2>Проверка</h2>
            <dl className="review-list">
              <dt>Группа</dt>
              <dd>{knownChat?.name ?? selection?.result?.chat?.title ?? "Выбрана"}</dd>
              <dt>Название</dt>
              <dd>{name || "Будет взято из Telegram"}</dd>
              <dt>Часовой пояс</dt>
              <dd>{zone}</dd>
            </dl>
            <div className="row">
              {!knownChat && <button onClick={() => setSelection(undefined)}>Выбрать другую</button>}
              <button className="primary" disabled={busy || !zone.trim()} onClick={create}>
                {busy ? "Создаём…" : "Создать клуб"}
              </button>
            </div>
          </>
        )}
        {error && <Notice kind="danger">{error}</Notice>}
      </Card>
    </Page>
  );
}

function CreateCamp({ overview, knownChat, done }: { overview?: AdminOverview; knownChat?: LockedAdminCommunity; done: () => void }) {
  const [name, setName] = useState(knownChat?.name ?? "");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [source, setSource] = useState<number | "">("");
  const [zone, setZone] = useState(Intl.DateTimeFormat().resolvedOptions().timeZone);
  const [selection, setSelection] = useState<PeerTicket>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [dateErrors, setDateErrors] = useState<{ start?: string; end?: string }>({});
  const sourceClub = overview?.clubs.find((club) => club.id === source);
  function changeSource(value: string) {
    const id = value ? +value : "";
    setSource(id);
    if (id) {
      const club = overview?.clubs.find((item) => item.id === id);
      if (club) setZone(club.timeZoneId);
    }
  }
  async function choose() {
    if (busy) return;
    const validation = campDateValidation(start, end);
    setDateErrors(validation);
    if (validation.start || validation.end) { setError(undefined); return; }
    setBusy(true);
    setError(undefined);
    try {
      const ticket = await selectPeer("CreateCampChat");
      setSelection(ticket);
      if (!name.trim() && ticket.result?.chat?.title) setName(ticket.result.chat.title);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function create() {
    if (busy) return;
    if (!selection && !knownChat) return;
    const validation = campDateValidation(start, end);
    setDateErrors(validation);
    if (validation.start || validation.end) { setError(undefined); return; }
    setBusy(true);
    setError(undefined);
    try {
      const result = await api<CommunityCreated>(
        "/admin/camps",
        json("POST", {
          selectionId: selection?.publicId,
          knownTelegramChatId: knownChat?.telegramChatId,
          name,
          startsAtLocal: start,
          endsAtLocal: end,
          sourceClubId: source || null,
          timeZoneId: zone,
        }),
      );
      telegram.success("Кэмп создан как черновик");
      if (result.warning) {
        telegram.warning();
        window.alert(result.warning);
      }
      done();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Новый кэмп" actions={<button onClick={done}>Назад</button>}>
      <Card className="form-grid">
        <Field label="Название" hint="После выбора группы подставим её название">
          <input value={name} maxLength={160} onChange={(e) => setName(e.target.value)} />
        </Field>
        <div className="date-range">
          <Field label="Начало кэмпа" error={dateErrors.start}>
            <input type="datetime-local" value={start} onChange={(e) => { setStart(e.target.value); setDateErrors({}); }} />
          </Field>
          <Field label="Окончание кэмпа" error={dateErrors.end}>
            <input type="datetime-local" min={start} value={end} onChange={(e) => { setEnd(e.target.value); setDateErrors({}); }} />
          </Field>
        </div>
        <Field label="Исходный клуб">
          <select value={source} onChange={(e) => changeSource(e.target.value)}>
            <option value="">Без базовой коллекции</option>
            {overview?.clubs.map((club) => (
              <option value={club.id} key={club.id}>
                {club.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Часовой пояс" hint={sourceClub ? "Унаследован от клуба; при необходимости измените" : "Выберите местное время кэмпа"}>
          <TimeZoneSelect value={zone} onChange={setZone} />
        </Field>
        {sourceClub && <Notice>Коллекция «{sourceClub.name}» будет скопирована один раз. Последующие изменения клуба кэмп не затронут.</Notice>}
        {(selection || knownChat) && (
          <>
            <h2>Проверка</h2>
            <dl className="review-list">
              <dt>Группа</dt>
              <dd>{knownChat?.name ?? selection?.result?.chat?.title ?? "Выбрана"}</dd>
              <dt>Даты</dt>
              <dd>
                {formatDate(start)} — {formatDate(end)}
              </dd>
              <dt>Основа</dt>
              <dd>{sourceClub?.name ?? "Пустая коллекция"}</dd>
              <dt>Часовой пояс</dt>
              <dd>{zone}</dd>
            </dl>
          </>
        )}
        {error && <Notice kind="danger">{error}</Notice>}
        <div className="row">
          {selection && !knownChat && <button onClick={() => setSelection(undefined)}>Выбрать другую группу</button>}
          <button className="primary" disabled={busy || !zone.trim()} onClick={selection || knownChat ? create : choose}>
            {busy ? "Ожидаем Telegram…" : selection || knownChat ? "Создать кэмп" : "Выбрать группу"}
          </button>
        </div>
      </Card>
    </Page>
  );
}

function Administrators({ communityKey, communityName, back }: { communityKey: string; communityName: string; back: () => void }) {
  const state = useAsync(() => api<Administrator[]>(`/admin/communities/${communityKey}/administrators`), [communityKey]);
  const candidates = useAsync(() => api<EligibleAdministrator[]>(`/admin/communities/${communityKey}/administrator-candidates`), [communityKey]);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  async function add() {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const selection = await selectPeer("AddAdministrator", communityKey);
      await api("/admin/administrators/from-selection", json("POST", { selectionId: selection.publicId, communityKey }));
      telegram.success("Администратор добавлен");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function remove(admin: Administrator) {
    if (busy) return;
    const name = admin.displayName ?? admin.telegramUsername ?? `Telegram ${admin.telegramUserId}`;
    if (!(await telegram.confirm(`Отозвать доступ администратора «${name}» к чату «${communityName}»?`))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/communities/${communityKey}/administrators/${admin.telegramUserId}`, { method: "DELETE" });
      telegram.success("Доступ отозван");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally { setBusy(false); }
  }
  async function approve(candidate: EligibleAdministrator) {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/communities/${communityKey}/administrators/${candidate.telegramUserId}`, { method: "POST" });
      telegram.success("Доступ выдан");
      state.reload();
      candidates.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page
      title="Администраторы"
      subtitle={communityName}
      actions={
        <div className="row">
          <button onClick={back}>Назад</button>
          <button className="primary" disabled={busy} onClick={add}>
            Добавить администратора
          </button>
        </div>
      }
    >
      <Notice>Доступ можно выдать только действующему администратору этого чата Telegram. Если его роль в Telegram будет снята, доступ OyinQ прекратится сразу.</Notice>
      {error && <Notice kind="danger">{error}</Notice>}
      <h2>Можно выдать доступ</h2>
      {candidates.loading ? (
        <Loading />
      ) : candidates.error ? (
        <Notice kind="warning">{candidates.error}</Notice>
      ) : !candidates.data?.length ? (
        <Empty>Новых администраторов Telegram для одобрения нет.</Empty>
      ) : (
        <div className="stack">
          {candidates.data.map((candidate) => (
            <Card key={candidate.telegramUserId}>
              <div className="row">
                <div>
                  <h3>{candidate.displayName ?? "Пользователь Telegram"}</h3>
                  {candidate.telegramUsername && <p>@{candidate.telegramUsername}</p>}
                  <small className="muted">ID {candidate.telegramUserId}</small>
                </div>
                <button className="primary" disabled={busy} onClick={() => approve(candidate)}>Выдать доступ</button>
              </div>
            </Card>
          ))}
        </div>
      )}
      <h2>Одобренные администраторы</h2>
      {state.loading ? (
        <Loading />
      ) : state.error ? (
        <ErrorState message={state.error} retry={state.reload} />
      ) : !state.data?.length ? (
        <Empty>Одобренных администраторов пока нет.</Empty>
      ) : (
        <div className="stack">
          {state.data.map((admin) => (
            <Card key={admin.telegramUserId}>
              <div className="row">
                <div>
                  <h3>{admin.displayName ?? "Пользователь Telegram"}</h3>
                  {admin.telegramUsername && <p>@{admin.telegramUsername}</p>}
                  <small className="muted">ID {admin.telegramUserId}</small>
                </div>
                <button className="danger ghost" onClick={() => remove(admin)}>
                  Отозвать доступ
                </button>
              </div>
            </Card>
          ))}
        </div>
      )}
    </Page>
  );
}

function Export({ isSuperAdmin }: { isSuperAdmin: boolean }) {
  const state = useAsync(() => api<AdminOverview>("/admin/overview"), []);
  const [error, setError] = useState<string>();
  const communities = [
    ...(state.data?.clubs ?? []).map((x) => ({
      key: x.communityKey,
      name: x.name,
    })),
    ...(state.data?.camps ?? []).map((x) => ({
      key: x.communityKey,
      name: x.name,
    })),
  ];
  return (
    <Page title="Экспорт">
      <Card>
        <h2>Данные чата</h2>
        <p>Каждый файл содержит данные только выбранного клуба или кэмпа.</p>
        <div className="stack">
          {communities.map((item) => (
            <button key={item.key} onClick={() => download(`/admin/exports/statistics.zip?community=${encodeURIComponent(item.key)}`, `oyinq-${item.key}-statistics.zip`).catch((e) => setError(e.message))}>
              Скачать «{item.name}»
            </button>
          ))}
        </div>
        {isSuperAdmin && <button onClick={() => download("/admin/exports/statistics.zip", "oyinq-all-statistics.zip").catch((e) => setError(e.message))}>Скачать все чаты</button>}
      </Card>
      <Notice>Коллекции клубов экспортируются отдельно на странице соответствующего клуба.</Notice>
      {error && <Notice kind="danger">{error}</Notice>}
    </Page>
  );
}

function ClubCollection({ clubId, bggAvailable, back }: { clubId: number; bggAvailable: boolean; back: () => void }) {
  type ClubImport = {
    publicId: string;
    bggUsername: string;
    status: string;
    stage?: BggImportStage;
    foundGames: number;
    foundExpansions: number;
    progressCurrent: number;
    progressTotal: number;
    addedGames: number;
    addedExpansions: number;
    orphanExpansions: number;
    error?: string;
  };
  const state = useAsync(() => api<ClubCollectionState>(`/admin/clubs/${clubId}/collection`), [clubId]);
  const [query, setQuery] = useState("");
  const [preview, setPreview] = useState<ClubGame>();
  const [selectedExpansions, setSelectedExpansions] = useState<number[]>([]);
  const [expandedGameId, setExpandedGameId] = useState<number>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState<{
    publicId: string;
    status: string;
    progressCurrent: number;
    progressTotal: number;
    error?: string;
  }>();
  const [bggInput, setBggInput] = useState("");
  const [clubImport, setClubImport] = useState<ClubImport>();
  useEffect(() => {
    if (!refresh || !["Queued", "Running"].includes(refresh.status)) return;
    const timer = window.setInterval(
      () =>
        api<typeof refresh>(`/admin/clubs/${clubId}/metadata-refresh/${refresh.publicId}`)
          .then((value) => {
            setRefresh(value);
            if (value.status === "Completed") state.reload();
          })
          .catch((reason) => setError(reason instanceof Error ? reason.message : String(reason))),
      3000,
    );
    return () => window.clearInterval(timer);
  }, [refresh?.publicId, refresh?.status, clubId]);
  useEffect(() => {
    if (!clubImport || !["Queued", "Running"].includes(clubImport.status)) return;
    const timer = window.setInterval(
      () =>
        api<ClubImport>(`/admin/clubs/${clubId}/bgg-imports/${clubImport.publicId}`)
          .then((value) => {
            setClubImport(value);
            if (value.status === "Completed") {
              telegram.success("Коллекция BGG добавлена");
              state.reload();
            }
          })
          .catch((reason) => setError(reason instanceof Error ? reason.message : String(reason))),
      3000,
    );
    return () => window.clearInterval(timer);
  }, [clubImport?.publicId, clubImport?.status, clubId]);
  const games = useMemo(() => searchGames(state.data?.collection.games ?? [], query), [state.data, query]);
  async function add() {
    if (busy || !state.data || !preview) return;
    setBusy(true);
    try {
      await api(
        `/admin/clubs/${clubId}/games`,
        json("POST", {
          expectedRevision: state.data.revision,
          bggInput: String(preview.bggId),
          expansionBggIds: selectedExpansions,
        }),
      );
      setPreview(undefined);
      setSelectedExpansions([]);
      telegram.success("Игра и данные BGG сохранены в коллекции");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      state.reload();
    } finally {
      setBusy(false);
    }
  }
  async function remove(id: number, name: string) {
    if (busy || !state.data || !(await telegram.confirm(`Удалить «${name}» из коллекции клуба?\n\nИгра исчезнет из каталога, но уже созданные сборы сохранят её снимок.`))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/clubs/${clubId}/games/${id}?expectedRevision=${state.data.revision}`, { method: "DELETE" });
      telegram.success("Игра удалена из коллекции");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      state.reload();
    } finally { setBusy(false); }
  }
  async function importFile(file: File) {
    if (busy || !state.data || !(await telegram.confirm(`Полностью заменить текущую коллекцию (${plural(state.data.collection.games.length, "игра", "игры", "игр")}) содержимым файла?\n\nУже созданные сборы сохранят свои снимки игр.`))) return;
    setBusy(true);
    setError(undefined);
    try {
      const document = JSON.parse(await file.text());
      await api(`/admin/clubs/${clubId}/collection`, json("PUT", { expectedRevision: state.data.revision, document }));
      telegram.success("Коллекция восстановлена");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally { setBusy(false); }
  }
  async function refreshMetadata() {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      setRefresh(
        await api(`/admin/clubs/${clubId}/metadata-refresh`, {
          method: "POST",
        }),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function importBgg() {
    if (busy || !bggInput.trim() || !(await telegram.confirm("Добавить все игры и связанные дополнения из коллекции BGG?\n\nТекущие игры и выбранные дополнения не будут удалены."))) return;
    setBusy(true);
    setError(undefined);
    try {
      setClubImport(await api<ClubImport>(`/admin/clubs/${clubId}/bgg-imports`, json("POST", { bggInput })));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Коллекция клуба" actions={<button onClick={back}>Назад</button>}>
      {state.loading ? (
        <Loading />
      ) : state.error || !state.data ? (
        <ErrorState message={state.error ?? "Коллекция не найдена"} retry={state.reload} />
      ) : (
        <>
          <div className="row">
            <Badge tone="accent">Ревизия {state.data.revision}</Badge>
            <span className="muted">Обновлено {new Date(state.data.updatedAt).toLocaleString("ru-RU")}</span>
          </div>
          {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Просмотр, поиск по коллекции, экспорт и восстановление продолжают работать.</Notice>}
          <Card>
            <h2>Добавить игру</h2>
            <GamePicker
              bggAvailable={bggAvailable}
              selected={preview}
              onSelect={(game) => {
                setPreview(game);
                setSelectedExpansions([]);
              }}
              onClear={() => {
                setPreview(undefined);
                setSelectedExpansions([]);
              }}
            />
            {preview && (
              <div className="selected-game">
                <div className="media">
                  <Cover src={preview.thumbnailImageUrl} name={preview.name} />
                  <div>
                    <h3>{preview.name}</h3>
                    <GameMeta game={preview} />
                  </div>
                </div>
                {preview.expansions.length > 0 && (
                  <fieldset>
                    <legend>Дополнения в коллекции</legend>
                    {preview.expansions.map((exp) => (
                      <label className="check" key={exp.bggId}>
                        <input type="checkbox" checked={selectedExpansions.includes(exp.bggId)} onChange={() => setSelectedExpansions((current) => (current.includes(exp.bggId) ? current.filter((id) => id !== exp.bggId) : [...current, exp.bggId]))} />
                        {exp.name}
                      </label>
                    ))}
                  </fieldset>
                )}
                <button className="primary" disabled={busy} onClick={add}>
                  {busy ? "Сохраняем…" : "Сохранить игру и дополнения"}
                </button>
              </div>
            )}
          </Card>
          <Card className="form-grid">
            <h2>Добавить коллекцию BGG</h2>
            <p className="muted">Одноразово добавляет все принадлежащие пользователю базовые игры и связанные дополнения. Уже сохранённые игры и дополнения не удаляются.</p>
            <Field label="Пользователь BGG" hint="Имя пользователя или ссылка на профиль">
              <input value={bggInput} maxLength={300} onChange={(event) => setBggInput(event.target.value)} placeholder="RollMoveClub" />
            </Field>
            <button disabled={!bggAvailable || busy || !bggInput.trim() || Boolean(clubImport && ["Queued", "Running"].includes(clubImport.status))} onClick={importBgg}>
              {busy ? "Запускаем импорт…" : "Добавить из BGG"}
            </button>
            {clubImport && <Notice kind={clubImport.status === "Failed" ? "danger" : clubImport.status === "Completed" ? "success" : "info"}>{clubImport.status === "Completed" ? clubImportResultText(clubImport) : clubImport.status === "Failed" ? (clubImport.error ?? "Не удалось импортировать коллекцию BGG.") : bggImportProgressText(clubImport)}</Notice>}
          </Card>
          <Card className="form-grid">
            <h2>Данные игр из BGG</h2>
            <p className="muted">Обновляет описания, изображения, возраст, время и таксономию. Состав коллекции и выбранные дополнения не меняются.</p>
            <button disabled={!bggAvailable || busy || Boolean(refresh && ["Queued", "Running"].includes(refresh.status))} onClick={refreshMetadata}>
              Обновить данные игр из BGG
            </button>
            {refresh && <Notice kind={refresh.status === "Failed" ? "danger" : refresh.status === "Completed" ? "success" : "info"}>{refresh.status === "Completed" ? "Данные обновлены." : refresh.status === "Failed" ? "Не удалось обновить данные BGG. Состав коллекции не изменён; повторите позже." : `Обработано ${refresh.progressCurrent} из ${refresh.progressTotal}`}</Notice>}
          </Card>
          <Card>
            <h2>Экспорт / восстановление</h2>
            <div className="row">
              <button onClick={() => download(`/admin/clubs/${clubId}/collection/export`, `club-${clubId}.json`)}>Скачать JSON</button>
              <label className="button">
                Импорт JSON
                <input hidden type="file" accept="application/json,.json" onChange={(e) => e.target.files?.[0] && importFile(e.target.files[0])} />
              </label>
            </div>
          </Card>
          <Field label="Поиск по коллекции">
            <input type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Название игры" />
          </Field>
          {error && <Notice kind="danger">{error}</Notice>}
          <div className="stack">
            {games.map((game) => (
              <Card key={game.bggId}>
                <div className="row collection-row">
                  <div className="media">
                    <Cover src={game.thumbnailImageUrl} name={game.name} />
                    <div>
                      <h3>{game.name}</h3>
                      <GameMeta game={game} />
                    </div>
                  </div>
                  <div className="row">
                    {hasAvailableExpansions(game) && (
                      <button aria-expanded={expandedGameId === game.bggId} onClick={() => setExpandedGameId((current) => toggleExpansionList(current, game))}>
                        Дополнения ({game.expansions.length})
                      </button>
                    )}
                    <button className="danger ghost" onClick={() => remove(game.bggId, game.name)}>
                      Удалить
                    </button>
                  </div>
                </div>
                {expandedGameId === game.bggId && (
                  <div className="selected-game">
                    <h4>Дополнения в коллекции</h4>
                    <ul>
                      {game.expansions.map((expansion) => (
                        <li key={expansion.bggId}>{expansion.name}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </Card>
            ))}
          </div>
        </>
      )}
    </Page>
  );
}

async function selectPeer(purpose: "AddAdministrator" | "CreateClubChat" | "CreateCampChat", communityKey?: string): Promise<PeerTicket> {
  const ticket = await api<PeerTicket>("/admin/peer-selections", json("POST", { purpose, communityKey }));
  const opened = ticket.preparedButtonId ? await telegram.requestPeer(ticket.preparedButtonId) : false;
  if (!opened)
    await api(`/admin/peer-selections/${ticket.publicId}/fallback`, {
      method: "POST",
    });
  for (let attempt = 0; attempt < 100; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 1500));
    const current = await api<PeerTicket>(`/admin/peer-selections/${ticket.publicId}`);
    if (current.status === "Completed") return current;
    if (current.status === "Consumed") throw new Error("Этот запрос выбора уже использован. Начните заново.");
    if (current.status === "Expired") throw new Error("Запрос выбора Telegram истёк.");
  }
  throw new Error("Telegram не вернул выбранный объект.");
}
