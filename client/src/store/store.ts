import { configureStore, createSlice, PayloadAction } from '@reduxjs/toolkit';
import { TypedUseSelectorHook, useDispatch, useSelector } from 'react-redux';
import { FilterSelected } from '../utils/interfaces/filter-selected';
import { Result, ResultWrap } from '../utils/interfaces/results';
import { DebugConfig } from '../utils/interfaces/debug-config';
import { Enums } from "../utils/interfaces/enums";
// Универсальный интерфейс для состояния (динамическая структура)
export interface IIndexable {
  [key: string]: any;
}

// Интерфейс для состояния приложения
export interface StateInterface extends IIndexable {
  isDebug:boolean;
  debigConfig:DebugConfig
  testP1: string;
  testP2:string;
  dataSourceSelected?:ResultWrap;
  isPopup: boolean;
  popUpType: Enums.PopupType;
  popUpObj: any;
  filterSelected:FilterSelected;
}

// Начальное состояние (может быть заполнено динамически)
const initialState: StateInterface = {
  isDebug: false,
  debigConfig:{config_1:'config_1_val'},
  testP1: 'testP1_456',
  testP2: 'testP2_2',
  dataSourceSelected: undefined,
  isPopup: false,
  popUpType: Enums.PopupType.none,
  popUpObj: null,
  filterSelected:{
    selected_name: "all", // спортсмен
    date: (new Date()).toString(), // Текущая дата
    date_str: new Date().toISOString().split("T")[0], // Строка в формате YYYY-MM-DD
    pool_type: "all",
    age: "all",
    club: "all",
    gender: "all",
    style_name: "",
    style_len: 0,
    training_table:{ mode: 'showTable' },
    rating_mode: 'no',
    filter_date_training_competition: 'training',
  },
};

// Основной slice для управления состоянием
const rootSlice = createSlice({
  name: 'root',
  initialState,
  reducers: {
    updateState: (state, action: PayloadAction<Partial<StateInterface>>) => {
      return {
        ...state,
        ...action.payload, // Позволяет обновлять любые поля состояния динамически
      };
    },
  },
});

// Создание Redux Store
const store = configureStore({
  reducer: rootSlice.reducer,
});

export const rootActions = rootSlice.actions;
export default store;

/**
 * Типы для работы с Redux (использование в селекторах и dispatch)
 */
export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;

// Хуки для работы с Redux
export const useAppDispatch = () => useDispatch<AppDispatch>();
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;
