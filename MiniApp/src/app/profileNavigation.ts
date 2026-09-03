export function profileGatheringVisit(currentCommunityKey: string, targetCommunityKey: string,
    gatheringId: string) {
  return { returnCommunityKey: currentCommunityKey, targetCommunityKey, gatheringId };
}
