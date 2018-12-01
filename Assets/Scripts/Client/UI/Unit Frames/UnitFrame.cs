﻿using Core;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

public class UnitFrame : MonoBehaviour
{
    [SerializeField, UsedImplicitly] private FillBar health;
    [SerializeField, UsedImplicitly] private FillBar mainResource;
    [SerializeField, UsedImplicitly] private Text unitName;

    private Unit unit;

    public void Initialize()
    {
        health = transform.Find("HealthBar").gameObject.GetComponent<FillBar>();
        unitName = transform.Find("Top Panel").Find("Unit Name").GetComponent<Text>();
        health.Initialize();
        mainResource = transform.Find("ResourceBar").gameObject.GetComponent<FillBar>();
        mainResource.Initialize();
    }

    public void UpdateFrame()
    {
        health.UpdateBar();
        mainResource.UpdateBar();
    }

    public void SetUnit(Unit newUnit)
    {
        unit = newUnit;
        if (unit != null)
        {
            gameObject.SetActive(true);
        }
        else
        {
            gameObject.SetActive(false);
            health.SetAttribute();
            mainResource.SetAttribute();
            unitName.text = "";
        }
    }

    public void OnTargetSet(Unit target)
    {
        gameObject.SetActive(true);
        unit = target;
    }

    public void OnTargetLost(Unit target)
    {
        gameObject.SetActive(false);
        unit = null;
        health.SetAttribute();
        mainResource.SetAttribute();
        unitName.text = "";
    }

    public void OnTargetSwitch(Unit target)
    {
        unit = target;
    }
}
