﻿//
//  SimulationController.cs
//  Tweenity
//
//  Modified by Vivian Gómez, inspired by the work of Matthew Ventures (https://www.mrventures.net/all-tutorials/converting-a-twine-story-to-unity)
//  Copyright © 2021 Vivian Gómez - Pablo Figueroa - Universidad de los Andes
//

using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using static SimulationObject;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

//Esta clase se encarga de controlar la simulación a partir de la lectura del grafo
//En particular, se encarga de ejecutar las acciones de simulador y detectar las acciones de usuario 
public class SimulationController : MonoBehaviour {

    //Este es el archivo de Twine en FORMATO ENTWEE 1.1.1 o ENTWEEDLE 1.0.1
    [SerializeField] public TextAsset twineText;
    public bool debugCarga = false;
    public bool debugLectura = false;

    //Guión de simulación actual
    SimulationScript currSim;

    //Nodo actual
    Node curNode;

    public delegate void NodeEnteredHandler( Node node );
    public event NodeEnteredHandler onEnteredNode;

    Action curReminder;
    Action curExpectedUserAction;
    List<Action> curSimulatorActions;

    //Este es el directorio que, por defecto, contiene los audios a reproducir durante la simulación
    //usando la función Simulator.Play("nombreAudio") 
    public static string currentDirectoryAudios = "TweenityInstructions";

    bool remember = true;
    bool timeout = true;

    CancellationTokenSource tokenSource;


    private void OnDisable() 
    {
        tokenSource.Cancel();
    }

    public Node GetCurrentNode() {
        return curNode;
    }

    private void PrintOnDebug(string msj)
    {
        if (debugLectura) Debug.Log(msj);
    }
    
    //Al empezar la aplicación se empieza la lectura del grafo (guión de simulación)
     void Start()
    {
        GameObject.Find("CanvasLogs").SetActive(debugLectura);
        tokenSource = new CancellationTokenSource();
        onEnteredNode += OnNodeEntered;
        InitializeDialogue();
    }

    //Se crea un objeto SimulationScript que modela el contenido del guión de simulación a partir del grafo
    public void InitializeDialogue() {
        currSim = new SimulationScript( twineText, debugCarga );
        curNode = currSim.GetStartNode();
        onEnteredNode( curNode );
    }

    public List<Response> GetCurrentResponses() {
        return curNode.responses;
    }

    public void ChooseResponse( int responseIndex ) {
        if(!curNode.tags.Contains("end"))
        {
            PrintOnDebug("responseIndex "+responseIndex);
            string nextNodeID = curNode.responses[responseIndex].destinationNode;
            PrintOnDebug("nextNodeID "+nextNodeID);
            Node nextNode = currSim.GetNode(nextNodeID);
            curNode = nextNode;
            onEnteredNode( nextNode );
        }
    }

    //Con este método se verifica si la acción de usuario realizada y reportada es la misma que se esperaba para el nodo actual
    //En dicho caso se selecciona el siguiente nodo
    public async void VerifyUserAction(Action actRecibida, int responseIndex=0)
    {
        PrintOnDebug("Se esperaba el objeto "+curExpectedUserAction.object2Action+" y se recibió "+actRecibida.object2Action);
        PrintOnDebug("Se esperaba la acción (método/función) "+curExpectedUserAction.actionName+" y se recibió "+actRecibida.actionName);
        PrintOnDebug("Se esperaban los parámetros "+curExpectedUserAction.actionParams+" y se recibieron "+actRecibida.actionParams);

        if(actRecibida.Equals(curExpectedUserAction) || curNode.userActions.Contains(actRecibida))
        {
            remember = false;
            timeout = false;
            ReminderController.HideReminder();
            PrintOnDebug("La función recibida es la esperada en el nodo actual");

            if(curSimulatorActions.Count>0)
            {
                try
                {
                    curExpectedUserAction = new Action();
                    MethodInfo taskObject = await ExecuteSimulatorActions(curSimulatorActions);

                    if (taskObject!=null)
                    {
                        SelectNextNode(actRecibida);
                    }
                }
                catch(System.OperationCanceledException)
                {
                    PrintOnDebug("Se canceló la tarea");
                } 

            }
            else
            {
                SelectNextNode(actRecibida);
            }
        }
        else
        {
            PrintOnDebug("Esta no es la acción de usuario esperada");
        }
    }

    //Para seleccionar el nodo siguiente se verifica si el actual es de tipo random, multiplechoice o sin tipo
    //Si es un nodo de selección aleatoria (random), se selecciona el siguiente de manera aleatoria entre la cantidad de posibles respuestas
    //Si es un nodo de selección múltiple (multiplechoice), se selecciona el siguiente nodo con base en la acción realizadaa, determinando su índice
    //Si es un nodo sin tipo, debe tener sólo un posible camino o nodo hijo, y este es el que se selecciona
    public void SelectNextNode(Action actRecibida)
    {
        // si tiene etiqueta random ==> se hace un random en el tamaño de responses
        if(GetCurrentNode().tags.Contains("random"))
        {
            PrintOnDebug("Se detecta que se realizó la acción esperada y se selecciona el nodo consecuencia de manera aleatoria");
            ChooseResponse(Random.Range(0,GetCurrentNode().responses.Count));
        }
        else if(GetCurrentNode().tags.Contains("multiplechoice"))
        {
            PrintOnDebug("Es MULTIPLE CHOICE");
            ChooseResponse(GetPositionOfResponse(actRecibida.object2Action+"."+actRecibida.actionName));
        }
        // sino se selecciona la 0
        else
        {
            if(curNode.responses.Count == 1)
            {
                PrintOnDebug("Se pasa al siguiente nodo");
                ChooseResponse(0);
            } 
            else if(GetCurrentNode().tags.Contains("timeout"))
            {
                PrintOnDebug("Era timeout, pero realizó la acción antes -> Se pasa al siguiente nodo");
                ChooseResponse(GetActionResponse(curNode.responses,GetPositionOfResponse("timeout")));
            } 
        }
    }

    // Ejecuta las acciones de simulador, esperando el tiempo que estas requieran 
    // (en el caso que tengan delay, como todos los métodos que empiezan con Play o el método Wait)
    public async Task<MethodInfo> ExecuteSimulatorActions(List<Action> simulatorActions)
    {
        PrintOnDebug("Empieza a ejecutar acciones de simulador actuales...");
        MethodInfo taskObject = null;
        foreach (var action in simulatorActions)
        {
            PrintOnDebug("Executing ... "+action.actionName+" - "+action.actionParams);
            GameObject objectF =  GameObject.Find(action.object2Action);
            if(objectF!=null)
            {
                taskObject = await objectF.GetComponent<ObjectController>().MethodAccess(action.actionName, action.actionParams);
            }
            else
            {
                PrintOnDebug("ERROR: No existe un objeto con el nombre "+action.object2Action);
            }
        }
        PrintOnDebug("Terminó acciones de simulador actuales...");

        return taskObject;
    }

    // Activa el recordatorio buscando el método ShowReminder
    public async void ActivarRecordatorio()
    {
        if(remember)
        {
            await GameObject.Find(curReminder.object2Action).GetComponent<ObjectController>().MethodAccess(curReminder.actionName, curReminder.actionParams);
        }
    }

    public void Timeout()
    {
        if(timeout)
        {
            ChooseResponse(GetPositionOfResponse("timeout"));
        }
    }

    public void SkipNode()
    {
        ChooseResponse(0);
    }


    private async void OnNodeEntered(Node newNode)
    {
        PrintOnDebug("CONTROLLER - Entering node: " + newNode.title);
        curExpectedUserAction = new Action();
        curSimulatorActions = null;
        MethodInfo taskObject = null;

        if(newNode.tags.Contains("end"))
        {
            try
            {
                await ExecuteSimulatorActions(newNode.simulatorActions);
                return;
            }
            catch(System.OperationCanceledException)
            {
                PrintOnDebug("Se canceló la tarea");
                return;
            } 
        }

        if(newNode.userActions.Count == 0)
        {
            try
            {
                taskObject = await ExecuteSimulatorActions(newNode.simulatorActions);
                if(newNode.tags.Contains("random"))
                {
                    ChooseResponse(Random.Range(0,newNode.responses.Count));
                }
                else
                {
                    if(newNode.responses.Count == 1 && taskObject!=null && !newNode.tags.Contains("dialogue"))
                    {
                        ChooseResponse(0);
                    }  
                }
            }
            catch(System.OperationCanceledException)
            {
                PrintOnDebug("Se canceló la tarea");
                return;
            }         
        }
        else{
            if(newNode.userActions.Count > 1 && curNode.tags.Contains("reminder"))
            {
                curReminder = newNode.userActions[0];
                // Invocar el recordatorio despues de x tiempo
                remember = true;
                Invoke("ActivarRecordatorio", float.Parse(curReminder.actionParams.Split(';')[0]));
                curExpectedUserAction = newNode.userActions[1];
            }
            else if(newNode.userActions.Count == 1)
            {
                curExpectedUserAction = newNode.userActions[0];
            }

            if(newNode.userActions.Count > 1 && curNode.tags.Contains("timeout"))
            {
                // Invocar el timeout despues de x tiempo
                timeout = true;
                Invoke("Timeout", float.Parse(newNode.userActions[0].actionParams));
                curExpectedUserAction = newNode.userActions[1];
            }
            else if(newNode.userActions.Count == 1)
            {
                curExpectedUserAction = newNode.userActions[0];
            }
            
            curSimulatorActions = newNode.simulatorActions;
        }
    }

    private int GetActionResponse(List<Response> responses, int posTimeout)
    {
        return responses.Count-1-posTimeout;
    }

    public int GetPositionOfResponse(string actionResponseText)
    {
        int res = -1;
        for (int i = 0; i < curNode.responses.Count; i++)
        {
            if(curNode.responses[i].destinationNode.StartsWith(actionResponseText))
            {
                res = i;
            }
        }
        return res;
    }
}