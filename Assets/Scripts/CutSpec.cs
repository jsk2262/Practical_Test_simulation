[System.Serializable]
public struct CutSpec
{
    public float widthCm;
    public float lengthCm;
    public float thicknessCm;

    public CutSpec(float widthCm, float lengthCm, float thicknessCm)
    {
        this.widthCm = widthCm;
        this.lengthCm = lengthCm;
        this.thicknessCm = thicknessCm;
    }
}
