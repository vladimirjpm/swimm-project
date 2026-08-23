/**
 * @deprecated Используйте отдельные хелперы напрямую:
 * HelperTime, HelperGender, HelperNormative, HelperResults, HelperSwimmer, HelperClub
 * 
 * Этот файл оставлен для обратной совместимости.
 */
import { NormativeLevelInfo } from '../interfaces/normative-level-info';
import { Result, TrainingGroup } from '../interfaces/results';
import HelperTime from './helper-time';
import HelperGender, { Gender } from './helper-gender';
import HelperNormative, { PoolType } from './helper-normative';
import HelperResults from './helper-results';
import HelperSwimmer from './helper-swimmer';
import HelperClub from './helper-club';
import HelperMedal from './helper-medal';

export default class Helper {
  // === Time ===
  static parseTimeToSeconds = HelperTime.parseTimeToSeconds;
  static formatSecondsToTimeString = HelperTime.formatSecondsToTimeString;

  // === Gender ===
  static resolveGender = HelperGender.resolveGender;
  static getGenderBgClass = HelperGender.getGenderBgClass;
  static resolveGenderFromEvent = HelperGender.resolveGenderFromEvent;

  // === Normative ===
  static resolvePoolType = HelperNormative.resolvePoolType;
  static getNormativeLevelInfo = HelperNormative.getNormativeLevelInfo;
  static isResultMasters = HelperNormative.isResultMasters;
  static isRecordHolder = HelperNormative.isRecordHolder;
  static isRecordTime = HelperNormative.isRecordTime;
  static getSwimmerRecords = HelperNormative.getSwimmerRecords;

  // === Results ===
  static ageLabel = HelperResults.ageLabel;
  static recordStepAge = HelperResults.recordStepAge;
  static sortByTime = HelperResults.sortByTime;
  static showTrainingTable = HelperResults.showTrainingTable;
  static groupTrainingByName = HelperResults.groupTrainingByName;
  static groupTrainingBySet = HelperResults.groupTrainingBySet;
  static getDolphinDataByName = HelperResults.getDolphinDataByName;
  static getDolphinDataGroupedByDate = HelperResults.getDolphinDataGroupedByDate;

  // === Swimmer ===
  static getBestResultsByStyle = HelperSwimmer.getBestResultsByStyle;
  static getAllResultsByName = HelperSwimmer.getAllResultsByName;
  static getMedalCountsByName = HelperSwimmer.getMedalCountsByName;
  static getInternationalPointsSumByName = HelperSwimmer.getInternationalPointsSumByName;

  // === Club ===
  static getClubsSummary = HelperClub.getClubsSummary;

  // === Medal ===
  static getMedalTier = HelperMedal.getMedalTier;
}
