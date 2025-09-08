using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CalibrationData
{
    [SerializeField] public string parentn, childn, tparentn, tchildn, rootn;
    [System.NonSerialized] public Transform parent, child, tparent, tchild, root;

    // 월드 기준(호환)
    [SerializeField] public Vector3 initialDir;
    [SerializeField] public Quaternion initialRotation;

    // 로컬 기준(핵심)
    [SerializeField] public Vector3 initialDirLocal;
    [SerializeField] public Quaternion initialLocalRotation;

    public Vector3 CurrentDirectionWorld => (tchild.position - tparent.position).normalized;

    public Vector3 CurrentDirectionLocal
    {
        get
        {
            if (!root) return CurrentDirectionWorld;
            Vector3 a = root.InverseTransformPoint(tparent.position);
            Vector3 b = root.InverseTransformPoint(tchild.position);
            return (b - a).normalized;
        }
    }

    public CalibrationData(Transform topParent, Transform fparent, Transform fchild, Transform tparent, Transform tchild)
    {
        this.parent = fparent; this.child = fchild; this.tparent = tparent; this.tchild = tchild; this.root = topParent;
        parentn = GetPath(parent); childn = GetPath(child); tparentn = GetPath(tparent); tchildn = GetPath(tchild); rootn = GetPath(topParent);

        // 초기 월드
        initialDir = (tchild.position - tparent.position).normalized;
        initialRotation = fparent.rotation;

        // 초기 로컬
        initialLocalRotation = fparent.localRotation;
        Vector3 a = topParent.InverseTransformPoint(tparent.position);
        Vector3 b = topParent.InverseTransformPoint(tchild.position);
        initialDirLocal = (b - a).normalized;
    }

    public CalibrationData ReconstructReferences()
    {
        SetFromPath(parentn, out parent);
        SetFromPath(childn, out child);
        SetFromPath(tparentn, out tparent);
        SetFromPath(tchildn, out tchild);
        SetFromPath(rootn, out root);
        return this;
    }

    private void SetFromPath(string path, out Transform target)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var go = GameObject.Find(path);
            target = go ? go.transform : null;
            return;
        }
        target = null;
    }

    private string GetPath(Transform child)
    {
        List<Transform> chain = new List<Transform>();
        while (child != null) { chain.Add(child); child = child.parent; }
        chain.Reverse();

        string s = "";
        foreach (Transform t in chain) s += t.name + "/";
        return s;
    }
}
