import React from 'react';
import './about.css';
import { useAppSelector } from '../../store/store';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ThemeDevTool from '../components/mix/theme-dev-tool/theme-dev-tool';


function About() {
  const testP1 = useAppSelector((store: { testP1: any; }) => store.testP1);
  const testP2 = useAppSelector((store: { testP2: any; }) => store.testP2);
  return (
    <>
      <AppTopbar active="about" />
      <div className="app abc text-4xl text-red">
         ABOUT 11 : {testP2} / {testP1}
      </div>
      <UI_ThemeDevTool />
    </>
  );
}

export default About;
