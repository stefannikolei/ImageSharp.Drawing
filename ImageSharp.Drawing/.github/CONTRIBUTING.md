# How to contribute to SixLabors.ImageSharp.Drawing

#### **Did you find a bug?**

- Please **ensure the bug was not already reported** by searching on GitHub under [Issues](https://github.com/SixLabors/ImageSharp.Drawing/issues).

- If you're unable to find an open issue addressing the problem, please [open a new one](https://github.com/SixLabors/ImageSharp.Drawing/issues/new). Be sure to include a **title, the applicable version, a clear description**, as much relevant information as possible, and a **code sample** or an **executable test case** demonstrating the expected behavior that is not occurring. Please do not hijack existing issues.

#### **Did you write a patch that fixes a bug?**

* Open a new GitHub pull request with the patch.

* Ensure the PR description clearly describes the problem and solution. Include the relevant issue number if applicable.

* Before submitting, please ensure that your code matches the existing coding patterns and practice as demonstrated in the repository. These follow strict Stylecop rules :cop:.

#### **Do you intend to add a new feature or change an existing one?**

* Suggest your change in the [Ideas Discussions Channel](https://github.com/SixLabors/ImageSharp.Drawing/discussions?discussions_q=category%3AIdeas) and start writing code.

* Do not open an issue on GitHub until you have collected positive feedback about the change. GitHub issues are primarily intended for bug reports and fixes.

#### **Building**

 * When first cloning the repo, make sure to run `git submodule update --init --recursive` otherwise the submodules (e.g. `shared-infrastructure`) will be missing.

 * Run `dotnet build` in the root of the repo, or open the ImageSharp.Drawing.sln file in Visual Studio and build from there.

#### **Running tests and Debugging**

* Expected test output is pulled in as a submodule from the [ImageSharp.Drawing.Tests.Images repository](https://github.com/SixLabors/ImageSharp.Drawing.Tests.Images/tree/main/ReferenceOutput). To succesfully run tests, make sure that you have updated the submodules!

#### **Do you have questions about consuming the library or the source code?**

* Ask any question about how to use SixLabors.ImageSharp.Drawing in the [Help Discussions Channel](https://github.com/SixLabors/ImageSharp.Drawing/discussions?discussions_q=category%3AHelp).

#### Code of Conduct  
This project has adopted the code of conduct defined by the [Contributor Covenant](https://contributor-covenant.org/) to clarify expected behavior in our community.

And please remember. SixLabors.ImageSharp.Drawing is the work of a very, very, small number of developers who struggle balancing time to contribute to the project with family time and work commitments. We encourage you to pitch in and help make our vision of simple accessible image processing available to all. Open Source can only exist with your help.

Thanks for reading!
