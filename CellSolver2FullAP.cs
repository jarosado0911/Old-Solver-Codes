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
using C2M2.Utils;
using Grid = C2M2.NeuronalDynamics.UGX.Grid;
namespace C2M2.NeuronalDynamics.Simulation
{
    public class CellSolver2FullAP : NDSimulation
    {
        //Set cell biological paramaters
        public const double res = 10.0;
        public const double gk = 36.0;
        public const double gna = 153.0;
        public const double gl = 0.3;
        public const double ek = -2.0;
        public const double ena = 112; //70;//112.0;
        public const double el = 0.6;
        public const double cap = 0.09;
        public const double ni = 0.5, mi = 0.4, hi = 0.2;       //state probabilities

        //Simulation parameters
        public const int nT = 40000;      // Number of Time steps
        public const double endTime = 50;  // End time value
        public const double vstart = 55;

        //Solution vectors
        private Vector U;
        private Vector M;
        private Vector N;
        private Vector H;

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
                curTimeSlice.Multiply(1, curTimeSlice);

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
            Timer timer = new Timer(nT + 1);
            timer.StartTimer();

            InitializeNeuronCell();
            // Computer simulation stepping parameters
            k = endTime / ((double)nT * 0.75); //Time step size
                                                      //double h = myCell.edgeLengths.Average();

            //double k = 0.001;
            //double h = k/0.9;
            double h = System.Math.Sqrt(2 * k) + 0.09;
            double alpha = 1e0; //for testing purposes only! Usually set to 1
            double a = 1;//1e-3; // I did a global node radius here
            double diffConst = (a / (2 * res * cap)) * alpha;

            double vRad;

            double cfl = diffConst * k / h;
            Debug.Log("cfl = " + cfl);

            // Stencil matrix for diffusion solve
            Matrix rhsM = Matrix.Build.Dense(NeuronCell.vertCount, NeuronCell.vertCount);

            // reaction vector
            Vector R = Vector.Build.Dense(NeuronCell.vertCount);

            // temporary voltage vector
            Vector tempV = Vector.Build.Dense(NeuronCell.vertCount);

            int nghbrCount;
            int nghbrInd;

            timer.StopTimer("Init");
            Debug.Log("Soma node neighbors" + NeuronCell.nodeData[0].neighborIDs.Count);
            Debug.Log("Last node neighbors" + NeuronCell.nodeData[631].neighborIDs.Count);

            rhsM[0, 0] = 1; //Set Soma coefficient to 1
            for (int p = 1; p < NeuronCell.vertCount - 1; p++)
            //for (int p = 0; p < myCell.vertCount; p++)
            {
                nghbrCount = NeuronCell.nodeData[p].neighborIDs.Count;
                vRad = NeuronCell.nodeData[p].nodeRadius;
                if (nghbrCount == 1)
                {
                    rhsM[p, p] = 1;
                }
                else
                {
                    rhsM[p, p] = 1 - nghbrCount * vRad * cfl / h;
                    for (int q = 0; q < nghbrCount; q++)
                    {
                        nghbrInd = NeuronCell.nodeData[p].neighborIDs[q];
                        rhsM[p, nghbrInd] = vRad * cfl / h;
                    }
                }

            }

            Debug.Log(rhsM);

            //this is for checking the boundary indices
            //for (int mm = 0; mm < myCell.boundaryID.Count; mm++)
            //{
            //    Debug.Log((myCell.boundaryID[mm]));
            //}

            int tCount = 0;

            try
            {
                for (i = 0; i < nT; i++)
                {
                    Debug.Log("Time counter = " + tCount);
                    timer.StartTimer();
                    mutex.WaitOne();

                    //Debug.Log("Elapsed Time = " + ((double)i) * k);
                    Debug.Log("U[0]:" + U[0] + "\n\tU[" + (300) + "]:" + U[300]);


                    // Diffusion solver
                    rhsM.Multiply(U, U);

                    // Save voltage from diffusion step for state probabilities
                    tempV.SetSubVector(0, NeuronCell.vertCount, U);

                    // Reaction
                    R.SetSubVector(0, NeuronCell.vertCount, reactF(U, N, M, H));
                    R.Multiply(k / cap, R);

                    // This is the solution for the voltage after the reaction is included!
                    U.Add(R, U);
                    //U[0] = 55;

                    //Now update state variables using FE on M,N,H
                    N.Add(fN(tempV, N).Multiply(k), N);
                    M.Add(fM(tempV, M).Multiply(k), M);
                    H.Add(fH(tempV, H).Multiply(k), H);

                    //U.Add(U, updateBC(myCell.vertCount,myCell.boundaryID, i));
                    //U.SetSubVector(0, myCell.vertCount, setBC(U, i, k, myCell.boundaryID));

                    //Always reset to IC conditions and boundary conditions (for now)
                    U.SetSubVector(0, NeuronCell.vertCount, boundaryConditions(U, NeuronCell.boundaryID));
                    tCount++;

                    mutex.ReleaseMutex();
                    timer.StopTimer("[cell " + i + "]:");
                }

            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            Debug.Log("Simulation Over.");
            Debug.Log("Time results:" + timer.ToString());
        }

        #region Local Functions
        private void InitializeNeuronCell()
        {
            //Initialize vector with all zeros
            U = Vector.Build.Dense(NeuronCell.vertCount);
            M = Vector.Build.Dense(NeuronCell.vertCount);
            N = Vector.Build.Dense(NeuronCell.vertCount);
            H = Vector.Build.Dense(NeuronCell.vertCount);

            //Set all initial state probabilities
            M.Add(mi, M);
            N.Add(ni, N);
            H.Add(hi, H);

            //Set the initial conditions of the solution
            U.SetSubVector(0, NeuronCell.vertCount, initialConditions(U, NeuronCell.boundaryID));
        }
        //Function for initialize voltage on cell
        public static Vector initialConditions(Vector V, List<int> bcIndices)
        {
            //Vector ic = Vector.Build.Dense(V.Count);
            V.Add(-15, V);
            V.SetSubVector(0, V.Count, boundaryConditions(V, bcIndices));
            //V[0] = -0.65;

            return V;
        }


        public static Vector boundaryConditions(Vector V, List<int> bcIndices)
        {
            for (int ind = 0; ind < bcIndices.Count; ind++)
            {
                V[bcIndices[ind]] = 55;
            }

            return V;

        }
        public static Vector setBC(Vector V, int tInd, double dt, List<int> bcIndices)
        {

            //uncomment this for loop to implement oscillating b.c.
            //for(int p=0; p<bcIndices.Count;p++)
            //{
            //    V[bcIndices[p]] = 0 * System.Math.Sin(tInd * dt * 0.09 * 3.1415962);
            //}

            return V;
        }

        public static Vector updateBC(int size, List<int> bcIndices, int tInd)
        {
            Vector bc = Vector.Build.Dense(size);

            return bc;
        }


        private static Vector reactF(Vector V, Vector NN, Vector MM, Vector HH)
        {
            Vector output = Vector.Build.Dense(V.Count);
            Vector prod = Vector.Build.Dense(V.Count);

            // Add current due to potassium
            prod = NN.PointwisePower(4);
            prod = (V - ek).PointwiseMultiply(prod);
            output = gk * prod;

            // Add current due to sodium
            prod = MM.PointwisePower(3);
            prod = HH.PointwiseMultiply(prod);
            prod = (V - ena).PointwiseMultiply(prod);
            output = output + gna * prod;

            // Add leak current
            output = output + gl * (V - el);

            // Return the negative of the total
            return -1 * output;
        }

        private static Vector fN(Vector V, Vector N)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = an(V).PointwiseMultiply(1 - N) - bn(V).PointwiseMultiply(N);

            return output;

        }

        private static Vector fM(Vector V, Vector M)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = am(V).PointwiseMultiply(1 - M) - bm(V).PointwiseMultiply(M);

            return output;

        }

        private static Vector fH(Vector V, Vector H)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = ah(V).PointwiseMultiply(1 - H) - bh(V).PointwiseMultiply(H);

            return output;

        }

        //The following functions are for the state variable ODEs on M,N,H
        private static Vector an(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);
            Vector temp = 10 - V;

            output = 0.01 * temp.PointwiseDivide((temp / 10).PointwiseExp() - 1);
            return output;
        }

        private static Vector bn(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = 0.125 * (-1 * V / 80).PointwiseExp();
            return output;
        }

        private static Vector am(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);
            Vector temp = 25 - V;

            output = 0.1 * temp.PointwiseDivide((temp / 10).PointwiseExp() - 1);
            return output;
        }

        private static Vector bm(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = 4 * (-1 * V / 18).PointwiseExp();
            return output;
        }

        private static Vector ah(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);

            output = 0.07 * (-1 * V / 20).PointwiseExp();
            return output;
        }

        private static Vector bh(Vector V)
        {
            Vector output = Vector.Build.Dense(V.Count);
            Vector temp = 30 - V;

            output = 1 / ((temp / 10).PointwiseExp() + 1);
            return output;
        }
        #endregion
    }
}