# Attention marks: data spec

Replaces the earlier `GET /me/marks` proposal. Most marks are derived on the client from responses the group page already loads (`useTrainingRsvp`, lane plan, sessions). The backend adds **three fields**, with no new endpoint.

## Derivation (client, `useTrainingMarks`)
| Mark | Who | Rule | Source |
|---|---|---|---|
| Red count | coach | `people.filter(p => p.answer == null && !p.on_break && !p.not_training).length > 0` | `TrainingRsvp.people` (next session) |
| Red dot | member | `can_answer && mine == null && !on_break` | `TrainingRsvp` |
| Gold dot | coach | `status === 'draft' \|\| unassigned.length > 0` | `LanePlan` for `next_training.date` |
| Gold dot | member | any `my_swimmers` entry with `moved_from_lane != null` | `LanePlan` **(new field 1)** |
| Accent dot | everyone | `new_media_count > 0` | `TrainingSessionList` **(new fields 2, 3)** |

Precedence per tab: red › gold › accent. If none applies, show no mark.

## Backend additions
1. **`LanePlan.my_swimmers[].moved_from_lane: number | null`**: the lane the coach moved this swimmer out of since the viewer last opened the plan. `null` means the swimmer was not moved, or the viewer has already seen the move. Mark the move seen on `GET /lane-plans/{date}` by a member.
2. **`TrainingSessionList.new_media_count: number`**: media added by others since `trainings_seen_at`.
3. **`PUT /api/hub-groups/{id}/sessions/seen`** sets `trainings_seen_at = now` for the viewer. Call it when the Trainings view opens. A per-media `is_new: boolean` drives the "3 NEW" label on entries.

## Group tab badge without opening the tab
The group page already calls `useTrainingRsvp` for the hero strip, so red marks cost nothing. For gold and accent marks on the group tab "Trainings", load the lane plan summary and the session list lazily after first paint. A combined `/me/marks` endpoint is only worth adding if this costs too much on the group list. Do not add it up front.
