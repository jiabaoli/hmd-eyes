﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using NetMQ;
using NetMQ.Sockets;
using MessagePack;

public class PupilDataReceiver : MonoBehaviour {

	private PupilSettings pupilSettings;

	private NetMQMessage message;
	private string messageType;
	private bool messageReceived;

	private MemoryStream mStream;

	private System.TimeSpan timeout;

	private bool contextExists = false;


	public RequestSocket _requestSocket;


	static PupilDataReceiver _Instance;
	public static PupilDataReceiver Instance
	{
		get{

			if (PupilGazeTracker.Instance == null && _Instance == null) {
				_Instance = new GameObject ("PupilDataReceiver").AddComponent<PupilDataReceiver> ();
			}

			return _Instance;
		
		}
	}
		

	#region Unity Methods

	void Update(){

		if (pupilSettings.connection.subscribeSocket != null)
			pupilSettings.connection.subscribeSocket.Poll ();

	}

	void Start(){

		pupilSettings = PupilSettings.Instance;

		if (!pupilSettings.connection.topicList.Contains ("pupil."))
			pupilSettings.connection.topicList.Add ("pupil.");

		if (!pupilSettings.connection.topicList.Contains ("gaze"))
			pupilSettings.connection.topicList.Add ("gaze");
		
		if (PupilDataReceiver._Instance == null)
			PupilDataReceiver._Instance = this;

		pupilSettings.connection.isConnected = false;
		pupilSettings.dataProcess.state = PupilSettings.EStatus.Idle;

		if (PupilSettings.Instance.connection.isAutorun)
			PupilTools.Connect ();
		
	}

	void OnApplicationQuit(){

		if (pupilSettings.dataProcess.state == PupilSettings.EStatus.Calibration)
			PupilTools.StopCalibration ();

		PupilTools.StopEyeProcesses ();

		Thread.Sleep (1);

		if (_requestSocket != null && PupilSettings.Instance.connection.isConnected) {
			_requestSocket.Close ();
			pupilSettings.connection.isConnected = false;
		}

		if (pupilSettings.connection.subscribeSocket != null)
			pupilSettings.connection.subscribeSocket.Close ();


		StopAllCoroutines ();

		PupilTools.RepaintGUI ();

		if (contextExists)
			NetMQConfig.ContextTerminate ();

	}

	#endregion

	#region Custom Methods

	public void OnConnected(){//Happens once on successful connection

		Debug.Log (" Succesfully connected to Pupil Service ! ");

		PupilTools.StartEyeProcesses ();

		PupilTools.RepaintGUI ();

		pupilSettings.connection.subscribeSocket = PupilTools.ClearAndInitiateSubscribe ();

		pupilSettings.dataProcess.state = PupilSettings.EStatus.ProcessingGaze;

		pupilSettings.connection.subscribeSocket.ReceiveReady += (s, a) => {

			int i = 0;


			NetMQMessage m = new NetMQMessage();

			while(a.Socket.TryReceiveMultipartMessage(ref m) && i<= 10){
			

				mStream = new MemoryStream(m[1].ToByteArray());

				string msgType = m[0].ConvertToString();

				if (pupilSettings.debug.printMessageType)
					print(msgType);

				if (pupilSettings.debug.printMessage)
					print (MessagePackSerializer.ToJson(m[1].ToByteArray()));

				switch(msgType){

				case "gaze":

					if (PupilData.Confidence (0) > 0.4f) {

						switch (pupilSettings.dataProcess.state) {

						case PupilSettings.EStatus.ProcessingGaze:

							PupilData.gazeDictionary = MessagePackSerializer.Deserialize<Dictionary<string,object>> (mStream);
							break;

						case PupilSettings.EStatus.Calibration:

							if (pupilSettings.calibration.initialized)
								PupilGazeTracker.Instance.Calibrate ();
							break;

						}

					}

					break;

				case "pupil.0":
					
					PupilData.pupil0Dictionary = MessagePackSerializer.Deserialize<Dictionary<string,object>> (mStream);//print("it is pupil 0 ! and the confidence level is : " + PupilData.Confidence(0) );
					break;

				case "pupil.1":
					
					PupilData.pupil1Dictionary = MessagePackSerializer.Deserialize<Dictionary<string,object>> (mStream);//print("it is pupil 1 ! and the confidence level is : " + PupilData.Confidence(1) );
					break;

				}

				i++;


			}
				

		};
			

	}

	public void RunConnect(){
	
		PupilDataReceiver.Instance.StartCoroutine (PupilDataReceiver.Instance.Connect (retry: true, retryDelay: 5f));

	}

	public IEnumerator Connect(bool retry = false, float retryDelay = 5f){

		print ("trying to connect coroutine beginning");
		yield return new WaitForSeconds (3f);
		print ("trying to connect coroutine after wait for 3 sec");

		PupilSettings pupilSettings = PupilSettings.Instance;

		while (!pupilSettings.connection.isConnected) {

			pupilSettings.connection.IPHeader = ">tcp://" + pupilSettings.connection.IP + ":";

			Debug.Log("Attempting to connect to : " + pupilSettings.connection.IPHeader + pupilSettings.connection.PORT);

			var timeout = new System.TimeSpan (0, 0, 1); //1sec

			if (!contextExists) {
				AsyncIO.ForceDotNet.Force ();
				NetMQConfig.ManualTerminationTakeOver ();
				NetMQConfig.ContextCreate (true);
				contextExists = true;
			}

			_requestSocket = new RequestSocket (pupilSettings.connection.IPHeader + pupilSettings.connection.PORT);
			_requestSocket.SendFrame ("SUB_PORT");


			pupilSettings.connection.isConnected = _requestSocket.TryReceiveFrameString (timeout, out pupilSettings.connection.subport);

			if (!pupilSettings.connection.isConnected) {

				if (retry) {
					
					Debug.LogWarning ("Could not connect, Re-trying in 5 seconds ! ");

//					NetMQConfig.con
//					NetMQConfig.ContextTerminate(true);

					yield return new WaitForSeconds (retryDelay);

				} else {
					
					NetMQConfig.ContextTerminate(true);

					yield break;

				}

			} else {

				OnConnected ();

				yield break;

			}

			yield return null;

		}


	}

	#endregion

	public PupilDataReceiver(){
	
		_Instance = this;
	
	}
		

}