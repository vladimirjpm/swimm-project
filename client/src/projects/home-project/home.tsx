import React from 'react';
import './home.css';
import { useAppSelector } from '../../store/store';


function Home() {
  const testP1 = useAppSelector((store: { testP1: any; }) => store.testP1);
  const testP2 = useAppSelector((store: { testP2: any; }) => store.testP2);
  return (
    <div className="app abc text-4xl">
      HOME-11 : {testP2} / {testP1}
    </div>
  );
}

export default Home;
