export type PlayerCountRange = {
  minimum: number;
  maximum: number;
  wasDefaulted: boolean;
};

export function normalizePlayerCountRange(minimum?: number, maximum?: number): PlayerCountRange {
  if (Number.isInteger(minimum) && Number.isInteger(maximum)
      && minimum! >= 1 && maximum! >= minimum!) {
    return { minimum: minimum!, maximum: maximum!, wasDefaulted: false };
  }

  return { minimum: 1, maximum: 12, wasDefaulted: true };
}
