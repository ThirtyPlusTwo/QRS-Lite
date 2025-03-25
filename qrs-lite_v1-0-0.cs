/*
QRS-Lite v1.0.0

CREATED AND BUGTESTED BY THIRTY-TWO

For reference, this takes much of its code from QRSv2.1.2. So, if you have experience with that,
then this will end up very similar in usage for simply the Active Steering component.
*/

// Enable/Disable Variable
private bool doActiveSteering = true;

// Active Steering
// Values for Front Wheels (Speeds => x coordinates, Angles => y coordinates)
private float[] frontWheelSpeeds = {25f, 70f, 80f, 95f, 100f};
private float[] frontWheelAngles = {44f, 42f, 40f, 35f, 33f};
// Values for Rear Wheels (Speeds => x coordinates, Angles => y coordinates)
private float[] rearWheelSpeeds = {25f, 70f, 100f};
private float[] rearWheelAngles = {18f, 3f, 0f};

// Active Steering Friction-Based Adjustment
// "frictionChangeFrictions" => x coordinates; "frontWheelAdjustment" & "rearWheelAdjustment" => y coordinates
// For usage of this, reach out to Thirty-Two for explanation. Otherwise, leave as all zeroes.
private float[] frictionChangeFrictions = {0f};
private float[] frontWheelAdjustment = {0f};
private float[] rearWheelAdjustment = {0f};

// This is if you want to have a specific Control Seat/Remote Control
private string mainControllerName = "Control Seat";

// DON'T CHANGE THINGS BELOW HERE UNLESS YOU KNOW WHAT YOU'RE DOING
private string programVersion = "1.0.0";
private string setupErrorMessage = "";
private int numSetupErrors = 0;

private bool doFrictionBasedAdjustment = false;
private float[] frontWheelSlopes;
private float[] frontWheelIntercepts;
private float[] rearWheelSlopes;
private float[] rearWheelIntercepts;
private float[] frontWheelAdjustmentSlopes;
private float[] frontWheelAdjustmentIntercepts;
private float[] rearWheelAdjustmentSlopes;
private float[] rearWheelAdjustmentIntercepts;

private IMyShipController _mainController;
private IMyMotorSuspension[] _suspensions;

public Program() {
	SetupControlSeat();
	SetupSuspensions();
	CheckArrayLengths();
	Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
	Me.CustomName = "PB QRS-Lite v" + programVersion;
	
	if (HandleErrors(numSetupErrors, setupErrorMessage)) { return; }
	
	DetermineFrictionBasedAdjustment();
	SetupSteeringArrays();
	Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Main(string argument) {
	if (HandleErrors(numSetupErrors, setupErrorMessage)) { return; }
	
	HandleActiveSteering();
	Echo("Running QRS-Lite v" + programVersion);
}

private void SetupControlSeat()
{
	var mainController = GridTerminalSystem.GetBlockWithName(mainControllerName) as IMyShipController;
	if (mainController != null) { _mainController = (IMyShipController)mainController; return; }
	
    var list = new List<IMyShipController>();

    GridTerminalSystem.GetBlocksOfType<IMyShipController>(list, c => c.CubeGrid == Me.CubeGrid);

    for (int i = 0; i < list.Count; i++)
    {
        if (list[i].BlockDefinition.ToString() == "MyObjectBuilder_Cockpit/PassengerSeatSmallOffset" || list[i].BlockDefinition.ToString() == "MyObjectBuilder_Cockpit/PassengerSeatSmallNew")
        {
            list.Remove(list[i]);
        }
    }

    if (list.Count == 0)
    {
        setupErrorMessage += "No valid IMyShipController Component found on craft.\n\n";
        numSetupErrors++;
    }
    if (setupErrorMessage != "") { return; }

    _mainController = (IMyShipController)list[0];
}

private void SetupSuspensions()
{
    var suspensions = new List<IMyMotorSuspension>();
    GridTerminalSystem.GetBlocksOfType(suspensions, s => s.CubeGrid == Me.CubeGrid);

    if (suspensions.Count != 4)
    {
        setupErrorMessage += "Only supports 4 suspensions.\n\n";
        numSetupErrors++;
    }
    if (setupErrorMessage != "") { return; }

    _suspensions = new IMyMotorSuspension[4];
    for (int i = 0; i < suspensions.Count; i++)
    {
        Vector3D worldDirection = suspensions[i].GetPosition() - _mainController.CenterOfMass;
        Vector3D bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(_mainController.WorldMatrix));

        if (bodyPosition.X < 0)
        {
            if (bodyPosition.Z < 0)
            {
                _suspensions[0] = (IMyMotorSuspension)suspensions[i];
            }
            else if (bodyPosition.Z > 0)
            {
                _suspensions[2] = (IMyMotorSuspension)suspensions[i];
            }
        }
        else if (bodyPosition.X > 0)
        {
            if (bodyPosition.Z < 0)
            {
                _suspensions[1] = (IMyMotorSuspension)suspensions[i];
            }
            else if (bodyPosition.Z > 0)
            {
                _suspensions[3] = (IMyMotorSuspension)suspensions[i];
            }
        }
    }
}

private void CheckArrayLengths() {
	if (frontWheelSpeeds.Length != frontWheelAngles.Length) {
		setupErrorMessage += "The number of elements in the \"frontWheelSpeeds\" and \"frontWheelAngles\" arrays do not match.\n\n";
		numSetupErrors++;
	}
	
	if (rearWheelSpeeds.Length != rearWheelAngles.Length) {
		setupErrorMessage += "The number of elements in the \"rearWheelSpeeds\" and \"rearWheelAngles\" arrays do not match.\n\n";
		numSetupErrors++;
	}
	
	if (frictionChangeFrictions.Length != frontWheelAdjustment.Length || frictionChangeFrictions.Length != rearWheelAdjustment.Length) {
		setupErrorMessage += "The number of elements in the Friction-Based Adjustment arrays do not all match.\n\n";
		numSetupErrors++;
	}
}

private bool HandleErrors(int setupErrors, string errorMessage)
{
    if (errorMessage != "")
    {
        Echo("Check the Programmable Block display for a list of errors. Click on \"Edit Text\" to see the whole message.");
        Me.GetSurface(0).WriteText("There are currently " + setupErrors + " setup errors:\n\n" + errorMessage);
        return true;
    }
    return false;
}

private void DetermineFrictionBasedAdjustment() {
	float totalFrictionChange = 0f;
	
	for (int i = 0; i < frictionChangeFrictions.Length; i++) {
		totalFrictionChange += Math.Abs(frontWheelAdjustment[i]) + Math.Abs(rearWheelAdjustment[i]);
	}
	
	doFrictionBasedAdjustment = (totalFrictionChange > 0f);
}

private void SetupSteeringArrays() {
	if (!doActiveSteering) { return; }
	
	frontWheelSlopes = CalculateSlopes(frontWheelSpeeds, frontWheelAngles);
    frontWheelIntercepts = CalculateIntercepts(frontWheelSpeeds, frontWheelAngles, frontWheelSlopes);
    rearWheelSlopes = CalculateSlopes(rearWheelSpeeds, rearWheelAngles);
    rearWheelIntercepts = CalculateIntercepts(rearWheelSpeeds, rearWheelAngles, rearWheelSlopes);
	
	if (!doFrictionBasedAdjustment) { return; }
	
	frontWheelAdjustmentSlopes = CalculateSlopes(frictionChangeFrictions, frontWheelAdjustment);
	frontWheelAdjustmentIntercepts = CalculateIntercepts(frictionChangeFrictions, frontWheelAdjustment, frontWheelAdjustmentSlopes);
	rearWheelAdjustmentSlopes = CalculateSlopes(frictionChangeFrictions, rearWheelAdjustment);
	rearWheelAdjustmentIntercepts = CalculateIntercepts(frictionChangeFrictions, rearWheelAdjustment, rearWheelAdjustmentSlopes);
}

private void HandleActiveSteering() {
	if (!doActiveSteering) { return; }
	
	float calculatedFrontAngle = 0f;
	float calculatedRearAngle = 0f;
	float carSpeed = (float)_mainController.GetShipSpeed();
	
	calculatedFrontAngle = RangedInterpolationCalculation(carSpeed, frontWheelSpeeds, frontWheelAngles, frontWheelSlopes, frontWheelIntercepts, 1);
	calculatedRearAngle = RangedInterpolationCalculation(carSpeed, rearWheelSpeeds, rearWheelAngles, rearWheelSlopes, rearWheelIntercepts, 1);
	
	if (doFrictionBasedAdjustment) {
		float averageWheelFriction = GetAverageWheelFriction(_suspensions);
		calculatedFrontAngle += RangedInterpolationCalculation(averageWheelFriction, frictionChangeFrictions, frontWheelAdjustment, frontWheelAdjustmentSlopes, frontWheelAdjustmentIntercepts, -1);
		calculatedRearAngle += RangedInterpolationCalculation(averageWheelFriction, frictionChangeFrictions, rearWheelAdjustment, rearWheelAdjustmentSlopes, rearWheelAdjustmentIntercepts, -1);
	}
	
	calculatedFrontAngle = (float)MathHelper.Clamp(calculatedFrontAngle, 0f, 46f);
	calculatedRearAngle = (float)MathHelper.Clamp(calculatedRearAngle, 0f, 46f);
	SetWheelSteeringDegrees(_suspensions, calculatedFrontAngle, calculatedRearAngle);
}

private void SetWheelSteeringDegrees(IMyMotorSuspension[] suspensions, float frontWheelDegrees, float rearWheelDegrees) {
	for (int i = 0; i < suspensions.Length; i++) {
		suspensions[i].MaxSteerAngle = (i < 2) ? frontWheelDegrees * 0.01745329f : rearWheelDegrees * 0.01745329f;
	}
}

private float RangedInterpolationCalculation(float xAxisValue, float[] xAxisBounds, float[] yAxisValues, float[] boundSlopes, float[] boundIntercepts, int comparisonDirection)
{
    float calculatedValue = 0f;
    for (int i = 0; i < xAxisBounds.Length; i++)
    {
        // This is expecting the bounds to be checked as x moves to +x
        if (xAxisValue <= xAxisBounds[i] && comparisonDirection == 1)
        {
            calculatedValue = (i == 0) ? yAxisValues[i] : boundSlopes[i - 1] * xAxisValue + boundIntercepts[i - 1];
            break;
        }
        // This is expecting the bounds to checked as x moves to 0
        if (xAxisValue >= xAxisBounds[i] && comparisonDirection == -1)
        {
            calculatedValue = (i == 0) ? yAxisValues[i] : boundSlopes[i - 1] * xAxisValue + boundIntercepts[i - 1];
            break;
        }
        calculatedValue = yAxisValues[i];
    }
    return calculatedValue;
}

private float[] CalculateSlopes(float[] xArray, float[] yArray)
{
    float[] calculatedArray = new float[xArray.Length - 1];
    for (int i = 0; i < xArray.Length - 1; i++)
    {
        calculatedArray[i] = (yArray[i + 1] - yArray[i]) / (xArray[i + 1] - xArray[i]);
    }
    return calculatedArray;
}

private float[] CalculateIntercepts(float[] xArray, float[] yArray, float[] slopesArray)
{
    float[] calculatedArray = new float[xArray.Length - 1];
    for (int i = 0; i < xArray.Length - 1; i++)
    {
        calculatedArray[i] = yArray[i] - slopesArray[i] * xArray[i];
    }
    return calculatedArray;
}

private float GetAverageWheelFriction(IMyMotorSuspension[] suspensions) {
	float calculatedFriction = 0;
	for (int i = 0; i < suspensions.Length; i++) {
		calculatedFriction += suspensions[i].Friction;
	}
	calculatedFriction /= suspensions.Length;
	return calculatedFriction;
}