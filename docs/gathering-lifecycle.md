# Gathering lifecycle

`GameGathering.Status` is a persisted state-machine value. It contains two families of states for historical compatibility, but they must never be treated as one undifferentiated “active” flag.

| Family | Statuses | Meaning |
| --- | --- | --- |
| Scheduled enrollment | `Recruiting`, `Ready`, `Full`, `Closed` | The gathering has not reached its start. The first three are derived from occupied seats; `Closed` is an organizer override. |
| Terminal lifecycle | `Completed`, `Cancelled` | The gathering is historical and no longer mutable. |

`GatheringLifecycle` owns the family and time-boundary semantics. Scheduled rows with `StartsAtUtc > now` are Upcoming. Scheduled rows at or before the boundary are temporarily History until the lifecycle worker either completes or hard-deletes them. `Completed` and `Cancelled` are always History regardless of scheduled time.

`GatheringCapacity` owns occupied seats and the derived open-enrollment status. Synchronization preserves manual `Closed` and terminal states. Increasing capacity promotes the existing waitlist in join order before the status is recalculated.

`GatheringAccessPolicy` owns every action exposed by adapters. The Mini App renders `canClose`, `canReopen`, `canCancel`, `canEdit`, `canJoin`, and `canLeave`; it does not reconstruct actions from the raw status.

All gathering mutations that can affect capacity or lifecycle use a serializable transaction and lock the gathering row before reading participants and guests. Database commit precedes Telegram publication and notifications.

## List contract

The canonical list scope is one of `upcoming`, `history`, `completed`, or `cancelled`. It is a single state, so contradictory combinations cannot exist inside the Mini App. During rollout the client also sends the equivalent legacy `view` and optional `status` parameters. The backend accepts matching canonical/legacy values and rejects contradictions. This keeps cached Telegram WebViews and rolling backend instances interoperable.
