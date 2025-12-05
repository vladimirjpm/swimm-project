export interface NormativeLevelInfo {
  currentLevel: string;
  currentLevelTime: string;
  nextLevel: string | null;
  nextTime: string | null;
  time: string | null;
  progressToNextLevel: number | null; // от 0 до 100%
  normativeAgeGroup?: string | null; // для masters
}