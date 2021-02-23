﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.IO;
using System;
using UnityEngine;

// These are the MathNet Numerics Libraries needed
// They need to dragged and dropped into the Unity assets plugins folder!
using SparseMatrix = MathNet.Numerics.LinearAlgebra.Double.SparseMatrix;
using Matrix = MathNet.Numerics.LinearAlgebra.Matrix<double>;
using Vector = MathNet.Numerics.LinearAlgebra.Vector<double>;
using Double = MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.Data.Text;
using solvers = MathNet.Numerics.LinearAlgebra.Double.Solvers;

using C2M2.NeuronalDynamics.UGX;
using Grid = C2M2.NeuronalDynamics.UGX.Grid;
namespace C2M2.NeuronalDynamics.Simulation
{
    public class CellSolver2SimpleFE : NDSimulation
    {
        //Set cell biological paramaters
        public const double res = 10.0;
        public const double gk = 36.0;
        public const double gna = 120.0;
        public const double gl = 0.3;
        public const double ek = -12.0;
        public const double ena = 220; //70;//112.0;
        public const double el = 0.6;
        public const double cap = 0.09;
        public const double ni = 0.5, mi = 0.4, hi = 0.2;       //state probabilities

        //Simulation parameters
        public const int nT = 100000;      // Number of Time steps
        public const double endTime = 25;  // End time value
        public const double vstart = 55;

        private Vector U;

        public override float GetSimulationTime() => i * (float)k;
        double k;
        // Keep track of i locally so that we know which simulation frame to send to other scripts
        private int i = -1;

        // Secnd simulation 1D values 
        public override double[] Get1DValues()
        {
            mutex.WaitOne();
            double[] curVals = null;
            if (i > -1)
            {
                Vector curTimeSlice = U.SubVector(0, NeuronCell.vertCount);
                curVals = curTimeSlice.ToArray();
            }
            mutex.ReleaseMutex();
            return curVals;
        }

        // Receive new simulation 1D index/value pairings
        public override void Set1DValues(Tuple<int, double>[] newValues)
        {
            mutex.WaitOne();
            foreach (Tuple<int, double> newVal in newValues)
            {
                int j = newVal.Item1;
                double val = newVal.Item2 * vstart;
                U[j] += val;
            }
            mutex.ReleaseMutex();
        }

        protected override void Solve()
        {
            InitializeNeuronCell();
            // Computer simulation stepping parameters
            k = endTime / (double)nT; //Time step size
                                             //double h = 0.008; // spatial step size

            //double h = myCell.edgeLengths.Average() * 1e4;

            //Debug.Log("h = " + h);
            //Debug.Log("Ave Edge Length = " + myCell.edgeLengths.Average());      


            for (i = 0; i < nT; i++)
            {
                mutex.WaitOne();
                //Debug.Log("Time step number = " + i);
                //Debug.Log("Elapsed Time = " + ((double)i) * k);
                Debug.Log("U[0]:" + U[0].ToString() + "\n\tU[" + (NeuronCell.vertCount - 1) + "]:" + U[NeuronCell.vertCount - 1].ToString());

                //This is the solver Vnxt = Vcur + k*f(Vcur)
                //Where f(Vcur)=2.5
                U.Add(2.5 * k, U);

                mutex.ReleaseMutex();
            }
            Debug.Log("Simulation Over.");
        }

        #region Local Functions
        private void InitializeNeuronCell()
        {
            //Initialize vector with all zeros
            U = Vector.Build.Dense(NeuronCell.vertCount);

            //Set the initial conditions of the solution
            U.SetSubVector(0, NeuronCell.vertCount, initialConditions(NeuronCell.vertCount));
        }
        //Function for initialize voltage on cell
        public static Vector initialConditions(int size)
        {
            Vector ic = Vector.Build.Dense(size);
            for (int ind = 0; ind < size; ind++)
            {
                ic[ind] = 0;
            }

            //ic[0] = 55;

            return ic;
        }
        #endregion
    }
}