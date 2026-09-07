export type BadgeTone = "neutral" | "accent" | "success" | "attention" | "danger" | "interest";

export function gatheringStatusTone(status: string | undefined): BadgeTone {
  switch (status) {
    case "Recruiting": return "attention";
    case "Ready":
    case "Full": return "success";
    case "Cancelled": return "danger";
    case "Closed":
    case "Completed":
    default: return "neutral";
  }
}

export function participationStatusTone(status: string | undefined): BadgeTone {
  switch (status) {
    case "Confirmed": return "success";
    case "Waitlisted": return "attention";
    case "Organizer": return "accent";
    case "Withdrawn":
    case "None":
    default: return "neutral";
  }
}

export function importStatusTone(status: string | undefined, stage?: string): BadgeTone {
  const value = status === "Running" && stage && stage !== "Queued" ? stage : status;
  switch (value) {
    case "Running":
    case "FetchingGames":
    case "FetchingExpansions":
    case "Preparing":
    case "Saving": return "accent";
    case "Completed":
    case "Confirmed": return "success";
    case "Failed": return "danger";
    case "Queued":
    case "Cancelled":
    default: return "neutral";
  }
}

export function attendanceOutcomeTone(outcome: string | undefined): BadgeTone {
  switch (outcome) {
    case "Attended": return "success";
    case "NoShow": return "danger";
    case "CancelledInAdvance":
    case "Unknown":
    default: return "neutral";
  }
}

export function campStatusTone(status: string | undefined): BadgeTone {
  if (status === "Active") return "success";
  if (status === "Cancelled") return "danger";
  return "neutral";
}
