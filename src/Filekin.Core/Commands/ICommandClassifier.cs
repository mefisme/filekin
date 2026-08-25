namespace Filekin.Core.Commands;

/// <summary>
/// Classifies a line of Files command-bar input into its route. Classification is pure text
/// analysis; it does not execute anything or touch the filesystem.
/// </summary>
public interface ICommandClassifier
{
    CommandClassification Classify(string input);
}
