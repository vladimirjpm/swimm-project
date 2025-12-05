import React from 'react';
import './about.css';
import { useAppSelector } from '../../store/store';


function About() {
  const testP1 = useAppSelector((store: { testP1: any; }) => store.testP1);
  const testP2 = useAppSelector((store: { testP2: any; }) => store.testP2);
  return (
    <div className="app abc text-4xl text-red">
       ABOUT 11 : {testP2} / {testP1}
    </div>
  );
}

export default About;
